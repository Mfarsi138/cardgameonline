namespace OhHell.Core;

public sealed class GameEngine
{
    private readonly List<PlayerState> players;
    private readonly Random random;
    private readonly Deck deck;
    private readonly List<TrickPlay> currentTrick = new();
    private readonly List<TrickPlay> lastCompletedTrick = new();
    private readonly List<string> eventLog = new();
    private Suit? leadSuit;
    private int totalBidsThisRound;

    public GameEngine(IEnumerable<PlayerDefinition> playerDefinitions, int? seed = null)
    {
        random = seed.HasValue ? new Random(seed.Value) : new Random();
        deck = new Deck(random);
        players = playerDefinitions.Select(definition => new PlayerState(definition.Name, definition.IsHuman)).ToList();

        if (players.Count < 3)
        {
            throw new InvalidOperationException("Oh Hell requires at least 3 players.");
        }

        MaxCardsPerRound = (52 - 1) / players.Count;
        Phase = GamePhase.NotStarted;
        StatusMessage = "Start a new match.";
    }

    public IReadOnlyList<PlayerState> Players => players;
    public IReadOnlyList<TrickPlay> CurrentTrick => currentTrick;
    public IReadOnlyList<TrickPlay> LastCompletedTrick => lastCompletedTrick;
    public IReadOnlyList<string> EventLog => eventLog;
    public GamePhase Phase { get; private set; }
    public int DealerIndex { get; private set; }
    public int LeaderIndex { get; private set; }
    public int CurrentTurnIndex { get; private set; }
    public int CardsPerPlayer { get; private set; }
    public int RoundNumber { get; private set; }
    public int MaxCardsPerRound { get; }
    public Card? TrumpCard { get; private set; }
    public string StatusMessage { get; private set; }
    public int? LastTrickWinnerIndex { get; private set; }
    public int? PendingTrickWinnerIndex { get; private set; }
    public bool IsTrickResolutionPending { get; private set; }
    public int TotalBidsThisRound => totalBidsThisRound;
    public int BidCountThisRound => players.Count(player => player.Bid.HasValue);
    public PlayerState CurrentPlayer => players[CurrentTurnIndex];
    public int? CurrentWinningPlayerIndex => currentTrick.Count == 0 || !leadSuit.HasValue
        ? null
        : DetermineWinningPlay(currentTrick, leadSuit.Value).PlayerIndex;
    public IReadOnlyList<PlayerState> MatchLeaders => players
        .Where(player => player.Score == players.Max(candidate => candidate.Score))
        .ToList();

    public static GameEngine CreateDefault() => CreateConfigured(new[]
    {
        new PlayerDefinition("You", true),
        new PlayerDefinition("North AI", false),
        new PlayerDefinition("East AI", false),
        new PlayerDefinition("West AI", false)
    });

    public static GameEngine CreateConfigured(IEnumerable<PlayerDefinition> definitions, int? seed = null) => new(definitions, seed);

    public void StartNewMatch()
    {
        foreach (var player in players)
        {
            player.Score = 0;
            player.RoundScoreDelta = 0;
        }

        DealerIndex = 0;
        CardsPerPlayer = MaxCardsPerRound;
        RoundNumber = 1;
        AddEvent("New match started.");
        StartRound();
    }

    public void AdvanceToNextRound()
    {
        if (Phase != GamePhase.RoundComplete)
        {
            throw new InvalidOperationException("Round is not complete.");
        }

        if (CardsPerPlayer <= 1)
        {
            Phase = GamePhase.MatchComplete;
            StatusMessage = "Match complete.";
            AddEvent("Match complete.");
            return;
        }

        DealerIndex = NextPlayerIndex(DealerIndex);
        CardsPerPlayer--;
        RoundNumber++;
        StartRound();
    }

    public IReadOnlyList<int> GetAllowedBids(int playerIndex)
    {
        ValidateCurrentPlayer(playerIndex, GamePhase.Bidding);
        var bids = Enumerable.Range(0, CardsPerPlayer + 1).ToList();
        var forbiddenBid = GetForbiddenBidForCurrentBidder();
        if (forbiddenBid.HasValue)
        {
            bids.Remove(forbiddenBid.Value);
        }

        return bids;
    }

    public void PlaceBid(int bid)
    {
        ValidateCurrentPlayer(CurrentTurnIndex, GamePhase.Bidding);

        var allowedBids = GetAllowedBids(CurrentTurnIndex);
        if (!allowedBids.Contains(bid))
        {
            throw new InvalidOperationException("Bid is not allowed.");
        }

        CurrentPlayer.Bid = bid;
        totalBidsThisRound += bid;
        AddEvent($"{CurrentPlayer.Name} bids {bid}.");

        if (AllPlayersBid())
        {
            Phase = GamePhase.TrickPlaying;
            CurrentTurnIndex = LeaderIndex;
            StatusMessage = $"Bidding complete. {CurrentPlayer.Name} leads the trick.";
            AddEvent("Bidding complete.");
            return;
        }

        CurrentTurnIndex = NextPlayerIndex(CurrentTurnIndex);
        StatusMessage = $"Waiting for bid from {CurrentPlayer.Name}.";
    }

    public IReadOnlyList<Card> GetLegalCards(int playerIndex)
    {
        ValidateCurrentPlayer(playerIndex, GamePhase.TrickPlaying);
        if (!leadSuit.HasValue)
        {
            return CurrentPlayer.Hand.ToList();
        }

        var matchingSuit = CurrentPlayer.Hand.Where(card => card.Suit == leadSuit.Value).ToList();
        return matchingSuit.Count > 0 ? matchingSuit : CurrentPlayer.Hand.ToList();
    }

    public void PlayCard(Card card)
    {
        ValidateCurrentPlayer(CurrentTurnIndex, GamePhase.TrickPlaying);

        if (IsTrickResolutionPending)
        {
            throw new InvalidOperationException("Resolve the current trick before playing again.");
        }

        if (!CurrentPlayer.Hand.Contains(card))
        {
            throw new InvalidOperationException("Card is not in the current player's hand.");
        }

        var legalCards = GetLegalCards(CurrentTurnIndex);
        if (!legalCards.Contains(card))
        {
            throw new InvalidOperationException("Player must follow suit.");
        }

        CurrentPlayer.Hand.Remove(card);
        if (!leadSuit.HasValue)
        {
            leadSuit = card.Suit;
        }

        currentTrick.Add(new TrickPlay(CurrentTurnIndex, card));
        AddEvent($"{CurrentPlayer.Name} plays {card.DisplayText}.");

        if (currentTrick.Count < players.Count)
        {
            CurrentTurnIndex = NextPlayerIndex(CurrentTurnIndex);
            var currentWinner = CurrentWinningPlayerIndex;
            StatusMessage = currentWinner.HasValue
                ? $"Current trick leader: {players[currentWinner.Value].Name}. Waiting for {CurrentPlayer.Name}."
                : $"Waiting for {CurrentPlayer.Name} to play.";
            return;
        }

        var winningPlay = DetermineWinningPlay(currentTrick, leadSuit!.Value);
        PendingTrickWinnerIndex = winningPlay.PlayerIndex;
        IsTrickResolutionPending = true;
        StatusMessage = $"All cards are on the table. {players[winningPlay.PlayerIndex].Name} is winning this trick.";
    }

    public void ResolvePendingTrick()
    {
        if (!IsTrickResolutionPending || !PendingTrickWinnerIndex.HasValue || !leadSuit.HasValue)
        {
            throw new InvalidOperationException("There is no pending trick to resolve.");
        }

        var winningPlay = DetermineWinningPlay(currentTrick, leadSuit.Value);
        var winnerIndex = winningPlay.PlayerIndex;

        players[winnerIndex].TricksWon++;
        lastCompletedTrick.Clear();
        lastCompletedTrick.AddRange(currentTrick);
        currentTrick.Clear();
        leadSuit = null;
        CurrentTurnIndex = winnerIndex;
        LeaderIndex = winnerIndex;
        LastTrickWinnerIndex = winnerIndex;
        PendingTrickWinnerIndex = null;
        IsTrickResolutionPending = false;

        AddEvent($"{players[winnerIndex].Name} wins the trick with {winningPlay.Card.DisplayText}.");

        if (players.All(player => player.Hand.Count == 0))
        {
            ApplyRoundScores();
            Phase = CardsPerPlayer == 1 ? GamePhase.MatchComplete : GamePhase.RoundComplete;
            StatusMessage = Phase == GamePhase.MatchComplete ? "Match complete." : "Round complete.";
            AddEvent(StatusMessage);
            return;
        }

        StatusMessage = $"{CurrentPlayer.Name} leads the next trick.";
    }

    public bool RunAiTurn()
    {
        if (IsTrickResolutionPending || Phase is GamePhase.MatchComplete or GamePhase.RoundComplete || CurrentPlayer.IsHuman)
        {
            return false;
        }

        if (Phase == GamePhase.Bidding)
        {
            PlaceBid(SelectBidForAi(CurrentTurnIndex));
            return true;
        }

        if (Phase == GamePhase.TrickPlaying)
        {
            PlayCard(SelectCardForAi(CurrentTurnIndex));
            return true;
        }

        return false;
    }

    public void RunAiTurnsUntilHuman()
    {
        while (RunAiTurn())
        {
        }
    }

    private void StartRound()
    {
        deck.Reset();
        deck.Shuffle();

        foreach (var player in players)
        {
            player.ResetForRound();
        }

        for (var dealIndex = 0; dealIndex < CardsPerPlayer; dealIndex++)
        {
            foreach (var player in players)
            {
                player.Hand.Add(deck.Draw());
            }
        }

        foreach (var player in players)
        {
            player.SortHand();
        }

        TrumpCard = deck.Draw();
        LeaderIndex = NextPlayerIndex(DealerIndex);
        CurrentTurnIndex = LeaderIndex;
        Phase = GamePhase.Bidding;
        totalBidsThisRound = 0;
        currentTrick.Clear();
        lastCompletedTrick.Clear();
        leadSuit = null;
        LastTrickWinnerIndex = null;
        PendingTrickWinnerIndex = null;
        IsTrickResolutionPending = false;

        AddEvent($"Round {RoundNumber} begins with {CardsPerPlayer} card(s) per player.");
        AddEvent($"Trump card is {TrumpCard.DisplayText}.");
        StatusMessage = $"Waiting for bid from {CurrentPlayer.Name}.";
    }

    private int? GetForbiddenBidForCurrentBidder()
    {
        var bidsPlaced = players.Count(player => player.Bid.HasValue);
        var isLastBidder = bidsPlaced == players.Count - 1;
        if (!isLastBidder)
        {
            return null;
        }

        return CardsPerPlayer - totalBidsThisRound;
    }

    private bool AllPlayersBid() => players.All(player => player.Bid.HasValue);

    private int SelectBidForAi(int playerIndex)
    {
        var player = players[playerIndex];
        var trumpCount = TrumpCard is null ? 0 : player.Hand.Count(card => card.Suit == TrumpCard.Suit);
        var acesAndKings = player.Hand.Count(card => card.Rank >= 13);
        var queensAndJacks = player.Hand.Count(card => card.Rank is 11 or 12);
        var longSuits = player.Hand
            .GroupBy(card => card.Suit)
            .Count(group => group.Count() >= 3);

        var estimate = trumpCount + acesAndKings + ((queensAndJacks + longSuits) / 2);
        var bid = Math.Clamp(estimate / 2, 0, CardsPerPlayer);
        var allowed = GetAllowedBids(playerIndex);

        if (!allowed.Contains(bid))
        {
            bid = allowed.OrderBy(value => Math.Abs(value - bid)).ThenBy(value => value).First();
        }

        return bid;
    }

    private Card SelectCardForAi(int playerIndex)
    {
        var legalCards = GetLegalCards(playerIndex).ToList();
        var player = players[playerIndex];
        var needsTricks = (player.Bid ?? 0) > player.TricksWon;
        var isLeading = currentTrick.Count == 0;

        if (isLeading)
        {
            return SelectLeadCardForAi(legalCards, needsTricks);
        }

        var currentBest = DetermineWinningPlay(currentTrick, leadSuit!.Value);
        var winningOptions = legalCards
            .Where(card => CompareCards(card, currentBest.Card, leadSuit!.Value) > 0)
            .OrderBy(card => ScoreCardStrength(card, leadSuit))
            .ThenBy(card => card.Rank)
            .ToList();

        if (needsTricks)
        {
            if (winningOptions.Count > 0)
            {
                return winningOptions.First();
            }

            return legalCards
                .OrderBy(card => ScoreCardStrength(card, leadSuit))
                .ThenBy(card => card.Rank)
                .First();
        }

        var safeLosers = legalCards
            .Where(card => CompareCards(card, currentBest.Card, leadSuit!.Value) <= 0)
            .OrderByDescending(card => ScoreCardStrength(card, leadSuit))
            .ThenByDescending(card => card.Rank)
            .ToList();

        if (safeLosers.Count > 0)
        {
            return safeLosers.First();
        }

        return winningOptions.Count > 0
            ? winningOptions.First()
            : legalCards.OrderBy(card => ScoreCardStrength(card, leadSuit)).ThenBy(card => card.Rank).First();
    }

    private Card SelectLeadCardForAi(List<Card> legalCards, bool needsTricks)
    {
        var nonTrumpCards = TrumpCard is null
            ? legalCards
            : legalCards.Where(card => card.Suit != TrumpCard.Suit).ToList();
        var pool = nonTrumpCards.Count > 0 ? nonTrumpCards : legalCards;

        if (needsTricks)
        {
            return pool
                .OrderByDescending(card => LeadStrength(card))
                .ThenByDescending(card => card.Rank)
                .First();
        }

        return pool
            .OrderBy(card => LeadStrength(card))
            .ThenBy(card => card.Rank)
            .First();
    }

    private int LeadStrength(Card card)
    {
        var trumpPenalty = TrumpCard is not null && card.Suit == TrumpCard.Suit ? 100 : 0;
        return card.Rank - trumpPenalty;
    }

    private int ScoreCardStrength(Card card, Suit? currentLeadSuit)
    {
        var trumpBoost = TrumpCard is not null && card.Suit == TrumpCard.Suit ? 100 : 0;
        var leadBoost = currentLeadSuit.HasValue && card.Suit == currentLeadSuit.Value ? 50 : 0;
        return trumpBoost + leadBoost + card.Rank;
    }

    private TrickPlay DetermineWinningPlay(IEnumerable<TrickPlay> trick, Suit currentLeadSuit)
    {
        var bestPlay = trick.First();
        foreach (var play in trick.Skip(1))
        {
            if (CompareCards(play.Card, bestPlay.Card, currentLeadSuit) > 0)
            {
                bestPlay = play;
            }
        }

        return bestPlay;
    }

    private int CompareCards(Card candidate, Card currentBest, Suit currentLeadSuit)
    {
        var candidateTrump = TrumpCard is not null && candidate.Suit == TrumpCard.Suit;
        var currentTrump = TrumpCard is not null && currentBest.Suit == TrumpCard.Suit;
        if (candidateTrump != currentTrump)
        {
            return candidateTrump ? 1 : -1;
        }

        var candidateLead = candidate.Suit == currentLeadSuit;
        var currentLead = currentBest.Suit == currentLeadSuit;
        if (candidateLead != currentLead)
        {
            return candidateLead ? 1 : -1;
        }

        return candidate.Rank.CompareTo(currentBest.Rank);
    }

    private void ApplyRoundScores()
    {
        foreach (var player in players)
        {
            var bid = player.Bid ?? 0;
            var exactBid = player.TricksWon == bid;
            var delta = exactBid ? 10 + bid : player.TricksWon;

            player.RoundScoreDelta = delta;
            player.Score += delta;
            AddEvent($"{player.Name}: bid {bid}, won {player.TricksWon}, round score +{delta}, total {player.Score}.");
        }
    }

    private void ValidateCurrentPlayer(int playerIndex, GamePhase expectedPhase)
    {
        if (Phase != expectedPhase)
        {
            throw new InvalidOperationException($"Expected phase {expectedPhase} but current phase is {Phase}.");
        }

        if (playerIndex != CurrentTurnIndex)
        {
            throw new InvalidOperationException("It is not this player's turn.");
        }
    }

    private int NextPlayerIndex(int index) => (index + 1) % players.Count;

    private void AddEvent(string message)
    {
        eventLog.Add(message);
        if (eventLog.Count > 60)
        {
            eventLog.RemoveAt(0);
        }
    }
}
