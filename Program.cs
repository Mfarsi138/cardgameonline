using System.Text;
using OhHell.Core;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
Console.Title = "Oh Hell Console";

while (true)
{
    var game = GameEngine.CreateDefault();
    game.StartNewMatch();
    RunMatch(game);

    Console.WriteLine();
    Console.Write("Play another match? (Y/N): ");
    var answer = (Console.ReadLine() ?? string.Empty).Trim();
    if (!answer.Equals("Y", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    Console.Clear();
}

static void RunMatch(GameEngine game)
{
    while (game.Phase != GamePhase.MatchComplete)
    {
        game.RunAiTurnsUntilHuman();
        Render(game);

        if (game.Phase == GamePhase.RoundComplete)
        {
            Console.WriteLine();
            Console.Write("Press Enter for next round...");
            Console.ReadLine();
            game.AdvanceToNextRound();
            Console.Clear();
            continue;
        }

        if (!game.CurrentPlayer.IsHuman)
        {
            continue;
        }

        if (game.Phase == GamePhase.Bidding)
        {
            PromptForBid(game);
        }
        else if (game.Phase == GamePhase.TrickPlaying)
        {
            PromptForCard(game);
        }

        Console.Clear();
    }

    Render(game);
    Console.WriteLine();
    Console.WriteLine("Match complete.");
}

static void PromptForBid(GameEngine game)
{
    var allowedBids = game.GetAllowedBids(game.CurrentTurnIndex).ToList();
    while (true)
    {
        Console.WriteLine();
        Console.Write($"Choose your bid [{string.Join(", ", allowedBids)}]: ");
        var input = Console.ReadLine();
        if (int.TryParse(input, out var bid) && allowedBids.Contains(bid))
        {
            game.PlaceBid(bid);
            return;
        }

        Console.WriteLine("Invalid bid.");
    }
}

static void PromptForCard(GameEngine game)
{
    var hand = game.CurrentPlayer.Hand;
    var legalCards = game.GetLegalCards(game.CurrentTurnIndex).ToList();

    while (true)
    {
        Console.WriteLine();
        Console.Write("Choose card index: ");
        var input = Console.ReadLine();
        if (!int.TryParse(input, out var index) || index < 1 || index > hand.Count)
        {
            Console.WriteLine("Invalid card index.");
            continue;
        }

        var selectedCard = hand[index - 1];
        if (!legalCards.Contains(selectedCard))
        {
            Console.WriteLine("You must follow suit if possible.");
            continue;
        }

        game.PlayCard(selectedCard);
        return;
    }
}

static void Render(GameEngine game)
{
    Console.Clear();
    Console.WriteLine("========================================");
    Console.WriteLine("Oh Hell");
    Console.WriteLine("========================================");
    Console.WriteLine($"Round: {game.RoundNumber}/{game.MaxCardsPerRound} | Cards: {game.CardsPerPlayer} | Phase: {game.Phase}");
    Console.WriteLine($"Dealer: {game.Players[game.DealerIndex].Name} | Leader: {game.Players[game.LeaderIndex].Name}");
    Console.Write("Trump: ");
    WriteCard(game.TrumpCard);
    Console.WriteLine();
    Console.WriteLine();

    Console.WriteLine("Players:");
    for (var i = 0; i < game.Players.Count; i++)
    {
        var player = game.Players[i];
        var leaderMarker = i == game.LeaderIndex ? "(▶)" : string.Empty;
        var dealerMarker = i == game.DealerIndex ? "(D)" : string.Empty;
        Console.WriteLine($"- {player.Name} {leaderMarker}{dealerMarker} | Bid: {FormatBid(player.Bid)} | Tricks: {player.TricksWon} | Score: {player.Score}");
    }

    Console.WriteLine();
    Console.WriteLine("Current trick:");
    if (game.CurrentTrick.Count == 0)
    {
        Console.WriteLine("- Waiting for leader");
    }
    else
    {
        foreach (var play in game.CurrentTrick)
        {
            Console.Write($"- {game.Players[play.PlayerIndex].Name}: ");
            WriteCard(play.Card);
            Console.WriteLine();
        }
    }

    if (game.LastCompletedTrick.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Last completed trick:");
        foreach (var play in game.LastCompletedTrick)
        {
            Console.Write($"- {game.Players[play.PlayerIndex].Name}: ");
            WriteCard(play.Card);
            Console.WriteLine();
        }
    }

    var human = game.Players.First(player => player.IsHuman);
    Console.WriteLine();
    Console.WriteLine("Your hand:");
    var legal = game.Phase == GamePhase.TrickPlaying && game.CurrentPlayer.IsHuman
        ? game.GetLegalCards(game.CurrentTurnIndex).ToHashSet()
        : human.Hand.ToHashSet();

    for (var i = 0; i < human.Hand.Count; i++)
    {
        var card = human.Hand[i];
        Console.Write($"{i + 1,2}. ");
        WriteCard(card);
        if (!legal.Contains(card))
        {
            Console.Write(" (blocked)");
        }
        Console.WriteLine();
    }

    Console.WriteLine();
    Console.WriteLine("Recent events:");
    foreach (var entry in game.EventLog.TakeLast(8))
    {
        Console.WriteLine($"- {entry}");
    }

    if (game.Phase != GamePhase.MatchComplete)
    {
        Console.WriteLine();
        Console.WriteLine($"Current turn: {game.CurrentPlayer.Name}");
        Console.WriteLine(game.StatusMessage);
    }
}

static string FormatBid(int? bid) => bid.HasValue ? bid.Value.ToString() : "-";

static void WriteCard(Card? card)
{
    if (card is null)
    {
        Console.Write("None");
        return;
    }

    var previous = Console.ForegroundColor;
    Console.ForegroundColor = card.Suit switch
    {
        Suit.Spades => ConsoleColor.White,
        Suit.Hearts => ConsoleColor.Red,
        Suit.Diamonds => ConsoleColor.Yellow,
        Suit.Clubs => ConsoleColor.Green,
        _ => previous
    };
    Console.Write(card.DisplayText);
    Console.ForegroundColor = previous;
}
