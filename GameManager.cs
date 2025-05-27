using System.Collections.Concurrent;

public class GameManager
{
    public ConcurrentDictionary<string, GameRoom> Rooms { get; } = new();
}