using SvinTusOnline.Models;
using System.Collections.Concurrent;

public class GameManager
{
    public ConcurrentDictionary<string, GameRoom> Rooms { get; } = new();

    private static readonly Random rnd = new();
    public static string[] FunnyAnimals = new[]
    {
        "Выдра", "Куница", "Пискулька", "Кулан", "Манул", "Жерлянка", "Нерпа", "Шилохвость"
    };

    public static string GetRandomName()
    {
        return FunnyAnimals[rnd.Next(FunnyAnimals.Length)] + " #" + rnd.Next(100, 999);
    }

    public static string GetAvatar(string name)
    {
        return $"/avatars/{name.Split(' ')[0].ToLower()}.png";
    }

    public void DealCards(GameRoom room)
    {
        foreach (var player in room.Players)
        {
            room.Hands[player.Id] = room.Deck.Take(5).ToList();
            room.Deck.RemoveRange(0, 5);
        }

    }
}

