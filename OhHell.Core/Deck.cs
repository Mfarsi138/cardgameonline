namespace OhHell.Core;

public sealed class Deck
{
    private readonly Random random;
    private readonly List<Card> cards = new();

    public Deck(Random random)
    {
        this.random = random;
        Reset();
    }

    public void Reset()
    {
        cards.Clear();
        foreach (var suit in Enum.GetValues<Suit>())
        {
            for (var rank = 2; rank <= 14; rank++)
            {
                cards.Add(new Card(suit, rank));
            }
        }
    }

    public void Shuffle()
    {
        for (var index = cards.Count - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
        }
    }

    public Card Draw()
    {
        var card = cards[^1];
        cards.RemoveAt(cards.Count - 1);
        return card;
    }
}
