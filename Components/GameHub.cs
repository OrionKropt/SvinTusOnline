using Microsoft.AspNetCore.SignalR;
using System.Timers;

public class GameHub : Hub
{
    private readonly GameManager _manager;
    private readonly System.Timers.Timer _timer;

    public GameHub(GameManager manager)
    {
        _manager = manager;
        _timer = new System.Timers.Timer(10000);
        _timer.Elapsed += CheckTimeouts;
        _timer.Start();
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

    private void StartGame(GameRoom room)
    {
        room.InitDeck();

        foreach (var player in room.Players.Values)
        {
            player.Hand = room.Deck.Take(5).ToList();
            for (int i = 0; i < 5; i++) room.Deck.Dequeue();
        }

        room.Discard.Add(room.Deck.Dequeue());
        room.CurrentTurn = room.Players.Keys.First();
        room.LastMoveTime = DateTime.UtcNow;
    }

    private async Task SendGameState(string roomCode)
    {
        if (!_manager.Rooms.TryGetValue(roomCode, out var room)) return;

        foreach (var player in room.Players.Values)
        {
            await Clients.Client(player.ConnectionId).SendAsync("GameState", new
            {
                Hand = player.Hand,
                DiscardTop = room.Discard.LastOrDefault(),
                Turn = room.CurrentTurn
            });
        }
    }

    private void CheckTimeouts(object? sender, ElapsedEventArgs e)
    {
        foreach (var room in _manager.Rooms.Values)
        {
            if ((DateTime.UtcNow - room.LastMoveTime).TotalSeconds > 15)
            {
                var keys = room.Players.Keys.ToList();
                int index = keys.IndexOf(room.CurrentTurn);
                int nextIndex = (index + 1) % keys.Count;
                room.CurrentTurn = keys[nextIndex];
                room.LastMoveTime = DateTime.UtcNow;
            }
        }
    }
}