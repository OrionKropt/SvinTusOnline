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
        var room = _manager.Rooms.GetOrAdd(roomCode, code => new GameRoom { RoomCode = code });
        var connectionId = Context.ConnectionId;

        if (room.IsStarted || room.Players.Count >= 8)
        {
            await Clients.Caller.SendAsync("RoomFull");
            return;
        }

        if (!room.Players.Any(p => p.Id == Context.ConnectionId))
        {
            var name = GameManager.GetRandomName();
            var player = new Player
            {
                Id = Context.ConnectionId,
                Name = name,
                Avatar = GameManager.GetAvatar(name)
            };
            room.Players.Add(player);
            room.Hands[player.Id] = new List<Card>();
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        await SendGameState(roomCode);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var room in _manager.Rooms.Values)
        {
            var player = room.Players.FirstOrDefault(p => p.Id == Context.ConnectionId);
            if (player != null)
            {
                await SendGameState(room.RoomCode);
                break;
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

 
private bool CardsMatch(Card top, Card newCard)
{
    return top.Color == newCard.Color || top.Number == newCard.Number;
}

public async Task PlayCard(string roomCode, string cardId)
    {
        if (!_manager.Rooms.TryGetValue(roomCode, out var room)) return;

        if (room.CurrentTurn != Context.ConnectionId)
        {
            await Clients.Caller.SendAsync("Error", "Сейчас не ваш ход.");
            return;
        }

        var hand = room.Hands[Context.ConnectionId];
        var card = hand.FirstOrDefault(c => c.Id == cardId);
        if (card == null) return;

        var top = room.Discard.LastOrDefault();
        if (top != null && !CardsMatch(top, card))
        {
            await Clients.Caller.SendAsync("Error", "Нельзя положить эту карту.");
            return;
        }

        hand.Remove(card);
        room.Discard.Add(card);

        if (hand.Count == 0)
        {
            await SendGameState(room.RoomCode);
            await Clients.Group(roomCode).SendAsync("Victory");
            return;
        }

        await AdvanceTurn(room);
        await SendGameState(roomCode);
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

    public async Task DrawCard(string roomCode)
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
