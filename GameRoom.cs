public class Card
{
    public string Color { get; set; } = "";
    public int Number { get; set; }
    public string Id => $"{Color}-{Number}";
}

public class Player
{
    public string ConnectionId { get; set; } = "";
    public List<Card> Hand { get; set; } = new();
}

public class GameRoom
{
    public string Code { get; set; } = "";
    public Dictionary<string, Player> Players { get; set; } = new();
    public Queue<Card> Deck { get; set; } = new();
    public List<Card> Discard { get; set; } = new();
    public string CurrentTurn { get; set; } = "";
    public DateTime LastMoveTime { get; set; } = DateTime.UtcNow;

    public void InitDeck()
    {
        Deck = new Queue<Card>(Shuffle(
            Enumerable.Range(1, 10)
                .SelectMany(n => new[] { "Red", "Green", "Blue", "Yellow" }
                .Select(c => new Card { Color = c, Number = n }))
        ));
    }

    private IEnumerable<T> Shuffle<T>(IEnumerable<T> source)
    {
        return source.OrderBy(_ => Guid.NewGuid());
    }
}