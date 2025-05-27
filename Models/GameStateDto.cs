namespace SvinTusOnline.Models;

public class GameStateDto
{
    public List<Card> Hand { get; set; } = new();
    public Card? DiscardTop { get; set; }
    public string? Turn { get; set; }
    public List<Player> Players { get; set; } = new();
    public Player CurrentPlayer { get; set; } = new();
    public int TimeLeftSeconds { get; set; }
    public DateTime TurnStartedAt { get; set; }
    public bool IsGameStarted {  get; set; }
}
