using Microsoft.AspNetCore.SignalR;
using SvinTusOnline.Models;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public class GameHub : Hub
{
    private readonly GameManager _manager;

    public GameHub(GameManager manager)
    {
        _manager = manager;
    }

    private async Task StartTurnTimer(GameRoom room)
    {
        room.TurnTimeoutCts?.Cancel();
        room.TurnTimeoutCts = new CancellationTokenSource();
        var token = room.TurnTimeoutCts.Token;

        var playerId = room.CurrentTurn;
        room.TurnStartedAt = DateTime.UtcNow;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(500, token); // проверка каждые 0.5 сек

                var elapsed = (DateTime.UtcNow - room.TurnStartedAt).TotalSeconds;

                // Ход уже сменился — значит, игрок успел сходить вручную
                if (room.CurrentTurn != playerId)
                    return;

                if (elapsed >= 20)
                {
                    Console.WriteLine($"[INFO] Время игрока {room.CurrentTurn} истекло.");
                    await Clients.Client(room.CurrentTurn!).SendAsync("TurnTimeout");

                    await DrawCard(room.RoomCode);
                    await AdvanceTurn(room);
                    return;
                }
            }
        }
        catch (TaskCanceledException)
        {
            // Ход завершён вручную — всё в порядке
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TIMER EXCEPTION] {ex.Message}");
        }
    }

    public async Task JoinRoom(string roomCode)
    {
        var room = _manager.Rooms.GetOrAdd(roomCode, code => new GameRoom { Code = code });
        if (!room.Players.ContainsKey(Context.ConnectionId))
        {
            room.Players[Context.ConnectionId] = new Player { ConnectionId = Context.ConnectionId };
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        await Clients.Group(roomCode).SendAsync("PlayerJoined", Context.ConnectionId);

        if (room.Players.Count >= 2 && room.Deck.Count == 0)
        {
            StartGame(room);
            await SendGameState(roomCode);
        }
    }

    public async Task PlayCard(string roomCode, string cardId)
    {
        if (!_manager.Rooms.TryGetValue(roomCode, out var room)) return;
        if (!room.Players.TryGetValue(Context.ConnectionId, out var player)) return;
        if (room.CurrentTurn != Context.ConnectionId) return;

        var card = player.Hand.FirstOrDefault(c => c.Id == cardId);
        var top = room.Discard.LastOrDefault();

        if (card != null && (card.Color == top?.Color || card.Number == top?.Number))
        {
            player.Hand.Remove(card);
            room.Discard.Add(card);
            room.LastMoveTime = DateTime.UtcNow;

            var keys = room.Players.Keys.ToList();
            int index = keys.IndexOf(Context.ConnectionId);
            int nextIndex = (index + 1) % keys.Count;
            room.CurrentTurn = keys[nextIndex];

            await SendGameState(roomCode);
        }
    }

    public async Task StartGame(string roomCode)
    {
        if (!_manager.Rooms.TryGetValue(roomCode, out var room)) return;
        if (room.IsStarted) return;
        room.IsStarted = true;
        room.InitDeck();

        _manager.DealCards(room);

        room.Discard.Add(room.Deck[0]);
        room.Deck.RemoveAt(0);

        room.CurrentTurn = room.Players.First().Id;

        await Clients.Group(roomCode).SendAsync("StartGameRedirect", roomCode);
        await SendGameState(roomCode);
        await AdvanceTurn(room);
    }


    private async Task SendGameState(string roomCode)
    {
        if (!_manager.Rooms.TryGetValue(roomCode, out var room)) return;

        var remainingTime = GetRemainingTime(room);

        foreach (var player in room.Players)
        {
            var state = new GameStateDto
            {
                Players = room.Players.Select(p => new Player
                {
                    Id = p.Id,
                    Name = p.Name,
                    Avatar = p.Avatar
                }).ToList(),
                CurrentPlayer = room.CurrentPlayer,
                Hand = room.Hands[player.Id],
                DiscardTop = room.Discard.LastOrDefault(),
                Turn = room.CurrentTurn,
                TimeLeftSeconds = GetRemainingTime(room),
                TurnStartedAt = room.TurnStartedAt
            };

            await Clients.Client(player.Id).SendAsync("GameState", state);
        }
    }

    private void CheckTimeouts(object? sender, ElapsedEventArgs e)
    {
        if (!_manager.Rooms.TryGetValue(roomCode, out var room)) return;
        if (room.Deck.Count == 0) return;

        var hand = room.Hands[Context.ConnectionId];
        hand.Add(room.Deck[0]);
        room.Deck.RemoveAt(0);
        await SendGameState(roomCode);
    }

    private async Task AdvanceTurn(GameRoom room)
    {
        var currentIndex = room.Players.FindIndex(p => p.Id == room.CurrentTurn);
        var nextIndex = (currentIndex + 1) % room.Players.Count;

        room.CurrentTurn = room.Players[nextIndex].Id;
        room.CurrentPlayer = room.Players[nextIndex];
        room.TurnStartedAt = DateTime.UtcNow;

        await SendGameState(room.RoomCode);

        _ = Task.Run(async () =>
        {
            try
            {
                await StartTurnTimer(room);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TIMER ERROR] {ex.Message}");
            }
        });
    }

    private int GetRemainingTime(GameRoom room)
    {
        var elapsed = (DateTime.UtcNow - room.TurnStartedAt).TotalSeconds;
        var remaining = 20 - elapsed;
        return Math.Max(0, (int)Math.Ceiling(remaining));
    }
}
