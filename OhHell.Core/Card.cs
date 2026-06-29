namespace OhHell.Core;

public sealed record Card(Suit Suit, int Rank)
{
    public string RankText => Rank switch
    {
        <= 10 => Rank.ToString(),
        11 => "J",
        12 => "Q",
        13 => "K",
        14 => "A",
        _ => "?"
    };

    public string SuitText => Suit switch
    {
        Suit.Spades => "♠",
        Suit.Hearts => "♥",
        Suit.Diamonds => "♦",
        Suit.Clubs => "♣",
        _ => "?"
    };

    public string SuitColorName => Suit switch
    {
        Suit.Spades => "white",
        Suit.Hearts => "red",
        Suit.Diamonds => "gold",
        Suit.Clubs => "green",
        _ => "inherit"
    };

    public string SuitCssClass => Suit switch
    {
        Suit.Spades => "suit-spades",
        Suit.Hearts => "suit-hearts",
        Suit.Diamonds => "suit-diamonds",
        Suit.Clubs => "suit-clubs",
        _ => string.Empty
    };

    public int SortSuitOrder => Suit switch
    {
        Suit.Spades => 0,
        Suit.Hearts => 1,
        Suit.Diamonds => 2,
        Suit.Clubs => 3,
        _ => 4
    };

    public string DisplayText => $"{RankText}{SuitText}";
}
