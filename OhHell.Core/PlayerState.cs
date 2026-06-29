namespace OhHell.Core;

public sealed class PlayerState
{
    public PlayerState(string name, bool isHuman)
    {
        Name = name;
        IsHuman = isHuman;
    }

    public string Name { get; }
    public bool IsHuman { get; }
    public List<Card> Hand { get; } = new();
    public int? Bid { get; set; }
    public int TricksWon { get; set; }
    public int Score { get; set; }
    public int RoundScoreDelta { get; set; }

    public void SortHand()
    {
        Hand.Sort((left, right) =>
        {
            var suitCompare = left.SortSuitOrder.CompareTo(right.SortSuitOrder);
            return suitCompare != 0 ? suitCompare : left.Rank.CompareTo(right.Rank);
        });
    }

    public void ResetForRound()
    {
        Hand.Clear();
        Bid = null;
        TricksWon = 0;
        RoundScoreDelta = 0;
    }
}
