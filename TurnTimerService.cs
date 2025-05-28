using Microsoft.AspNetCore.SignalR;
using SvinTusOnline.Models;
using System.Collections.Concurrent;

public class TurnTimerService
{
    private readonly GameManager _manager;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();

    public TurnTimerService(GameManager manager,
                            IHubContext<GameHub> hubContext)
    {
        _manager = manager;
        _hubContext = hubContext;
    }

    public void StartTimer(string roomCode)
    {
        CancelTimer(roomCode);

        var room = _manager.Rooms[roomCode];
        room.TurnStartedAt = DateTime.UtcNow;
        var playerId = room.CurrentTurn;

        var cts = new CancellationTokenSource();
        _timers[roomCode] = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), token);
                if (!token.IsCancellationRequested && room.CurrentTurn == playerId)
                {
                    await _hubContext
                                .Clients
                                .Client(playerId!)
                                .SendAsync("TurnTimeout");
                    await Task.Delay(100);
                    var idx = room.Players.FindIndex(p => p.Id == playerId);
                    var next = room.Players[(idx + 1) % room.Players.Count].Id;
                    room.CurrentTurn = next;
                    room.TurnStartedAt = DateTime.UtcNow;
                    foreach (var p in room.Players)
                    {
                        var state = new GameStateDto
                        {
                            Players = room.Players,
                            Hand = room.Hands[p.Id],
                            DiscardTop = room.Discard.LastOrDefault(),
                            Turn = room.CurrentTurn,
                            TurnStartedAt = room.TurnStartedAt
                        };
                        await _hubContext
                            .Clients
                            .Client(p.Id)
                            .SendAsync("GameState", state);
                    }

                    StartTimer(roomCode);
                }
            }
            catch (TaskCanceledException) { }
        });
    }

    public void CancelTimer(string roomCode)
    {
        if (_timers.TryRemove(roomCode, out var cts))
            cts.Cancel();
    }
}
