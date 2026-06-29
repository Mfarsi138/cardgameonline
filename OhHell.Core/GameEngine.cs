namespace OhHell.Core;

public sealed class GameEngine
{
    private readonly List<PlayerState> players;
    private readonly Random random;
    private readonly Deck deck;
    private readonly List<TrickPlay> currentTrick = new();
    private readonly List<TrickPlay> lastCompletedTrick = new();
    private readonly List<string> eventLog = new();
    private readonly HashSet<Card> playedCardsThisRound = new();
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
        playedCardsThisRound.Add(card);
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
        playedCardsThisRound.Clear();

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
        var hand = player.Hand;
        var maxCards = CardsPerPlayer;

        double estimate = 0;

        foreach (var suit in Enum.GetValues<Suit>())
        {
            var suitCards = hand.Where(c => c.Suit == suit)
                .OrderByDescending(c => c.Rank).ToList();
            var isTrump = TrumpCard is not null && suit == TrumpCard.Suit;
            var count = suitCards.Count;

            if (count == 0)
            {
                if (!isTrump) estimate += 0.45;
                continue;
            }

            if (isTrump)
            {
                foreach (var card in suitCards)
                {
                    if (card.Rank >= 14) estimate += 0.95;
                    else if (card.Rank >= 13) estimate += 0.85;
                    else if (card.Rank >= 12) estimate += 0.65;
                    else if (card.Rank >= 11) estimate += 0.45;
                    else if (card.Rank >= 10) estimate += 0.25;
                    else estimate += 0.10;
                }
            }
            else
            {
                var highest = suitCards[0].Rank;
                if (highest >= 14) estimate += 0.9;
                if (count >= 2 && highest >= 13 && suitCards[1].Rank >= 12)
                    estimate += 0.55;
                if (count == 1 && highest < 12)
                    estimate += 0.2;
            }
        }

        var roundFraction = (double)maxCards / MaxCardsPerRound;
        if (roundFraction < 0.35) estimate = Math.Max(0, estimate - 1.0);
        else if (roundFraction < 0.5) estimate = Math.Max(0, estimate - 0.5);

        var distance = (playerIndex - CurrentTurnIndex + players.Count) % players.Count;
        if (distance <= 1) estimate -= 0.3;
        else if (distance >= players.Count - 2) estimate += 0.3;

        var bid = (int)Math.Round(estimate, MidpointRounding.AwayFromZero);
        bid = Math.Clamp(bid, 0, maxCards);
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
            return SelectLeadCardForAi(playerIndex, legalCards, needsTricks);
        }

        var currentBest = DetermineWinningPlay(currentTrick, leadSuit!.Value);
        var currentWinnerIndex = currentBest.PlayerIndex;

        var winningOptions = legalCards
            .Where(card => CompareCards(card, currentBest.Card, leadSuit!.Value) > 0)
            .OrderBy(card => ScoreCardStrength(card, leadSuit))
            .ThenBy(card => card.Rank)
            .ToList();

        if (needsTricks)
        {
            return SelectCardWhenNeedsTricks(player, legalCards, winningOptions, currentBest, currentWinnerIndex);
        }

        return SelectCardWhenDumping(player, legalCards, winningOptions, currentBest);
    }

    private Card SelectCardWhenNeedsTricks(
        PlayerState player, List<Card> legalCards, List<Card> winningOptions,
        TrickPlay currentBest, int currentWinnerIndex)
    {
        if (winningOptions.Count == 0)
        {
            return legalCards
                .OrderBy(card => ScoreCardStrength(card, leadSuit))
                .ThenBy(card => card.Rank)
                .First();
        }

        var isSelfWinning = currentWinnerIndex == GetPlayerIndex(player);
        if (isSelfWinning)
        {
            var currentBestCard = currentBest.Card;
            var guaranteedStronger = winningOptions
                .Where(c => CompareCards(c, currentBestCard, leadSuit!.Value) > 0)
                .OrderBy(c => ScoreCardStrength(c, leadSuit))
                .ThenBy(c => c.Rank)
                .ToList();
            if (guaranteedStronger.Count > 0)
            {
                return guaranteedStronger.First();
            }
        }

        var trumpWins = winningOptions
            .Where(c => TrumpCard is not null && c.Suit == TrumpCard.Suit)
            .ToList();

        if (trumpWins.Count > 0 && legalCards.Any(c => TrumpCard is null || c.Suit != TrumpCard.Suit))
        {
            var minTrump = trumpWins
                .OrderBy(c => c.Rank)
                .First();
            return minTrump;
        }

        return winningOptions
            .OrderBy(c => ScoreCardStrength(c, leadSuit))
            .ThenBy(c => c.Rank)
            .First();
    }

    private Card SelectCardWhenDumping(
        PlayerState player, List<Card> legalCards, List<Card> winningOptions,
        TrickPlay currentBest)
    {
        var safeLosers = legalCards
            .Where(card => CompareCards(card, currentBest.Card, leadSuit!.Value) <= 0)
            .OrderByDescending(card => ScoreCardStrength(card, leadSuit))
            .ThenByDescending(card => card.Rank)
            .ToList();

        if (safeLosers.Count > 0)
        {
            return safeLosers.First();
        }

        if (winningOptions.Count > 0)
        {
            var canUnderTrump = winningOptions
                .Where(c => TrumpCard is not null && c.Suit == TrumpCard.Suit)
                .OrderBy(c => c.Rank)
                .ToList();
            if (canUnderTrump.Count > 0 && canUnderTrump.Count < winningOptions.Count)
            {
                return canUnderTrump.First();
            }
        }

        return winningOptions.Count > 0
            ? winningOptions.First()
            : legalCards.OrderBy(card => ScoreCardStrength(card, leadSuit)).ThenBy(card => card.Rank).First();
    }

    private Card SelectLeadCardForAi(int playerIndex, List<Card> legalCards, bool needsTricks)
    {
        if (TrumpCard is null)
        {
            return SelectLeadNoTrump(legalCards, needsTricks);
        }

        var trumpCards = legalCards.Where(c => c.Suit == TrumpCard.Suit).ToList();
        var nonTrumpCards = legalCards.Where(c => c.Suit != TrumpCard.Suit).ToList();

        if (needsTricks)
        {
            return SelectLeadWhenNeedsTricks(playerIndex, legalCards, trumpCards, nonTrumpCards);
        }

        return SelectLeadWhenDumping(playerIndex, legalCards, trumpCards, nonTrumpCards);
    }

    private Card SelectLeadWhenNeedsTricks(
        int playerIndex, List<Card> legalCards, List<Card> trumpCards, List<Card> nonTrumpCards)
    {
        var suitLengths = GetSuitLengths(legalCards, playerIndex);
        var player = players[playerIndex];

        var shortSuits = suitLengths
            .Where(kvp => kvp.Key != TrumpCard!.Suit && kvp.Value == 1 && player.Hand.Any(c => c.Suit == kvp.Key && c.Rank >= 13))
            .Select(kvp => kvp.Key)
            .ToList();

        if (shortSuits.Count > 0)
        {
            var targetSuit = shortSuits.First();
            var aceCard = player.Hand.First(c => c.Suit == targetSuit && c.Rank >= 13);
            if (legalCards.Contains(aceCard))
            {
                return aceCard;
            }
        }

        if (nonTrumpCards.Count > 0)
        {
            var suitGroups = nonTrumpCards
                .GroupBy(c => c.Suit)
                .OrderBy(g => g.Count())
                .ThenByDescending(g => g.Max(c => c.Rank));

            foreach (var group in suitGroups)
            {
                var topCard = group.OrderByDescending(c => c.Rank).First();
                if (group.Any(c => c.Rank >= 13) && group.Count() <= 2)
                {
                    return topCard;
                }
            }

            return nonTrumpCards
                .OrderByDescending(c => c.Rank)
                .First();
        }

        return trumpCards
            .OrderBy(c => c.Rank)
            .First();
    }

    private Card SelectLeadWhenDumping(
        int playerIndex, List<Card> legalCards, List<Card> trumpCards, List<Card> nonTrumpCards)
    {
        if (nonTrumpCards.Count > 0)
        {
            var suitGroups = nonTrumpCards
                .GroupBy(c => c.Suit)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Max(c => c.Rank));

            foreach (var group in suitGroups)
            {
                if (group.Count() >= 3)
                {
                    return group.OrderBy(c => c.Rank).First();
                }
            }

            return nonTrumpCards
                .OrderBy(c => c.Rank)
                .First();
        }

        return trumpCards
            .OrderByDescending(c => c.Rank)
            .First();
    }

    private Card SelectLeadNoTrump(List<Card> legalCards, bool needsTricks)
    {
        if (needsTricks)
        {
            return legalCards
                .OrderByDescending(c => c.Rank)
                .First();
        }

        return legalCards
            .OrderBy(c => c.Rank)
            .First();
    }

    private Dictionary<Suit, int> GetSuitLengths(List<Card> legalCards, int playerIndex)
    {
        var player = players[playerIndex];
        return Enum.GetValues<Suit>().ToDictionary(s => s, s => player.Hand.Count(c => c.Suit == s));
    }

    private int GetPlayerIndex(PlayerState player)
    {
        for (var i = 0; i < players.Count; i++)
        {
            if (players[i] == player) { return i; }
        }

        return -1;
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
