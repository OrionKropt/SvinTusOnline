using Microsoft.AspNetCore.SignalR;
using SvinTusOnline.Models;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public class GameHub : Hub
{
    private readonly GameManager _manager;
    private readonly TurnTimerService _turnTimer;

    public GameHub(GameManager manager, TurnTimerService turnTimer)
    {
        _manager = manager;
        _turnTimer = turnTimer;
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

 
    private bool CardsMatch(Card top, Card newCard) =>
        top.Color == newCard.Color || top.Number == newCard.Number;

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
                Hand = room.Hands[player.Id],
                DiscardTop = room.Discard.LastOrDefault(),
                Turn = room.CurrentTurn,
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
        var idx = room.Players.FindIndex(p => p.Id == room.CurrentTurn);
        var next = room.Players[(idx + 1) % room.Players.Count].Id;

        room.CurrentTurn = next;
        room.TurnStartedAt = DateTime.UtcNow;
        _turnTimer.StartTimer(room.RoomCode);

        await SendGameState(room.RoomCode);
    }
}
