using SvinTusOnline.Models;

public class GameRoom
{
    public string RoomCode { get; set; } = "";
    public List<Player> Players { get; set; } = new();
    public bool IsStarted { get; set; } = false;

    public Dictionary<string, List<Card>> Hands { get; set; } = new();
    public List<Card> Deck { get; set; } = new();
    public List<Card> Discard { get; set; } = new();
    public Player CurrentPlayer { get; set; } = new();
    public string? CurrentTurn { get; set; }
    public CancellationTokenSource? TurnTimeoutCts { get; set; }
    public int TurnSecondsLeft { get; set; }
    public DateTime TurnStartedAt { get; set; }

    public void InitDeck()
    {
        Deck = new Queue<Card>(Shuffle(
            Enumerable.Range(0, 8)
                .SelectMany(n => new[] { "Blue", "Red", "Green", "Orange" }
                .Select(c => new Card { Color = c, Number = n }))
        )).ToList();
    }

    private IEnumerable<T> Shuffle<T>(IEnumerable<T> source)
    {
        return source.OrderBy(_ => Guid.NewGuid());
    }
}
