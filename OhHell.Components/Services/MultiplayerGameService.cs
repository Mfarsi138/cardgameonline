using System.Collections.Concurrent;
using OhHell.Core;
using OhHell.Components.Models;

namespace OhHell.Components.Services;

public sealed class MultiplayerGameService
{
    private const int BidDelayMs = 450;
    private const int PlayDelayMs = 500;
    private const int RevealWinnerDelayMs = 2200;
    private const int AfterResolveDelayMs = 180;
    private const int TurnTimeoutMs = 15_000;

    private readonly ConcurrentDictionary<string, OnlineGameRoom> rooms = new();
    private readonly ConcurrentDictionary<string, System.Threading.Timer> turnTimers = new();
    private readonly ConcurrentDictionary<string, bool> turnTimerFired = new();

    public event Action<string>? RoomUpdated;

    public OnlineGameRoom CreateRoom(string sessionId, string playerId, string playerName, int maxPlayers, string boardTheme, string cardTheme, string botDifficulty)
    {
        var normalizedName = NormalizePlayerName(playerName, "Host");
        var room = new OnlineGameRoom
        {
            RoomCode = GenerateRoomCode(),
            HostSessionId = sessionId,
            MaxPlayers = Math.Clamp(maxPlayers, 3, 6),
            BoardTheme = boardTheme,
            CardTheme = cardTheme
        };

        room.Members.Add(new OnlineRoomMember
        {
            SessionId = sessionId,
            PlayerId = NormalizePlayerId(playerId, sessionId),
            Name = normalizedName,
            IsHost = true
        });

        rooms[room.RoomCode] = room;
        Notify(room.RoomCode);
        return room;
    }

    public OnlineGameRoom? JoinRoom(string roomCode, string sessionId, string playerId, string playerName)
    {
        if (!rooms.TryGetValue(roomCode.Trim().ToUpperInvariant(), out var room))
        {
            return null;
        }

        lock (room.Members)
        {
            var normalizedPlayerId = NormalizePlayerId(playerId, sessionId);
            var existingMember = room.Members.FirstOrDefault(member => member.PlayerId == normalizedPlayerId);
            if (existingMember is not null)
            {
                if (room.HostSessionId == existingMember.SessionId)
                {
                    room.HostSessionId = sessionId;
                }

                existingMember.SessionId = sessionId;
                return room;
            }

            if (room.Members.Any(member => member.SessionId == sessionId))
            {
                return room;
            }

            if (room.Started || room.Members.Count >= room.MaxPlayers)
            {
                return null;
            }

            var normalizedName = NormalizePlayerName(playerName, $"Player {room.Members.Count + 1}");
            if (room.Members.Any(member => string.Equals(member.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            room.Members.Add(new OnlineRoomMember
            {
                SessionId = sessionId,
                PlayerId = normalizedPlayerId,
                Name = normalizedName,
                IsHost = false
            });
        }

        Notify(room.RoomCode);
        return room;
    }

    public OnlineGameRoom? GetRoom(string roomCode)
    {
        rooms.TryGetValue(roomCode.Trim().ToUpperInvariant(), out var room);
        return room;
    }

    public IReadOnlyList<OnlineRoomSummary> GetActiveRooms(string? playerId)
    {
        var normalizedPlayerId = string.IsNullOrWhiteSpace(playerId) ? null : playerId.Trim();

        return rooms.Values
            .Select(room =>
            {
                lock (room.Members)
                {
                    var host = room.Members.FirstOrDefault(member => member.IsHost)?.Name ?? "Host";
                    var canRejoin = normalizedPlayerId is not null && room.Members.Any(member => member.PlayerId == normalizedPlayerId);
                    var canJoin = !room.Started && room.Members.Count < room.MaxPlayers;

                    return new OnlineRoomSummary
                    {
                        RoomCode = room.RoomCode,
                        HostName = host,
                        PlayerCount = room.Members.Count,
                        MaxPlayers = room.MaxPlayers,
                        Started = room.Started,
                        BoardTheme = room.BoardTheme,
                        CardTheme = room.CardTheme,
                        CanJoin = canJoin,
                        CanRejoin = canRejoin
                    };
                }
            })
            .OrderBy(summary => summary.Started)
            .ThenBy(summary => summary.RoomCode, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<bool> StartGameAsync(string roomCode, string sessionId)
    {
        var room = GetRoom(roomCode);
        if (room is null)
        {
            return false;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            var member = room.Members.FirstOrDefault(entry => entry.SessionId == sessionId);
            var canResetCompletedMatch = room.Engine?.Phase == GamePhase.MatchComplete;
            if (member is null)
            {
                return false;
            }

            if (!canResetCompletedMatch && (room.HostSessionId != sessionId || room.Started))
            {
                return false;
            }

            if (room.Started && !canResetCompletedMatch)
            {
                return false;
            }

            var definitions = new List<PlayerDefinition>();
            for (var index = 0; index < room.Members.Count; index++)
            {
                room.Members[index].PlayerIndex = index;
                definitions.Add(new PlayerDefinition(room.Members[index].Name, true));
            }

            for (var botIndex = room.Members.Count; botIndex < room.MaxPlayers; botIndex++)
            {
                definitions.Add(new PlayerDefinition($"Bot {botIndex}", false));
            }

            room.Engine = GameEngine.CreateConfigured(definitions);
            room.Engine.StartNewMatch();
            room.Started = true;
        }
        finally
        {
            room.SyncLock.Release();
        }

        Notify(room.RoomCode);
        await RunAutomaticFlowAsync(room.RoomCode);
        return true;
    }

    public async Task<bool> PlaceBidAsync(string roomCode, string sessionId, int bid)
    {
        CancelTurnTimer(roomCode);
        var room = GetRoom(roomCode);
        if (room?.Engine is null)
        {
            return false;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            var member = room.Members.FirstOrDefault(entry => entry.SessionId == sessionId);
            if (member?.PlayerIndex != room.Engine.CurrentTurnIndex || room.Engine.Phase != GamePhase.Bidding)
            {
                return false;
            }

            room.Engine.PlaceBid(bid);
        }
        finally
        {
            room.SyncLock.Release();
        }

        Notify(room.RoomCode);
        await RunAutomaticFlowAsync(room.RoomCode);
        return true;
    }

    public async Task<bool> PlayCardAsync(string roomCode, string sessionId, Card card)
    {
        CancelTurnTimer(roomCode);
        var room = GetRoom(roomCode);
        if (room?.Engine is null)
        {
            return false;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            var member = room.Members.FirstOrDefault(entry => entry.SessionId == sessionId);
            if (member?.PlayerIndex != room.Engine.CurrentTurnIndex || room.Engine.Phase != GamePhase.TrickPlaying)
            {
                return false;
            }

            room.Engine.PlayCard(card);
        }
        finally
        {
            room.SyncLock.Release();
        }

        Notify(room.RoomCode);
        await RunAutomaticFlowAsync(room.RoomCode);
        return true;
    }

    public async Task<bool> AdvanceRoundAsync(string roomCode, string sessionId)
    {
        var room = GetRoom(roomCode);
        if (room?.Engine is null)
        {
            return false;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            if (room.Engine.Phase != GamePhase.RoundComplete)
            {
                return false;
            }

            room.Engine.AdvanceToNextRound();
        }
        finally
        {
            room.SyncLock.Release();
        }

        Notify(room.RoomCode);
        await RunAutomaticFlowAsync(room.RoomCode);
        return true;
    }

    public async Task LeaveRoomAsync(string roomCode, string sessionId)
    {
        CancelTurnTimer(roomCode);
        var room = GetRoom(roomCode);
        if (room is null)
        {
            return;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            var member = room.Members.FirstOrDefault(entry => entry.SessionId == sessionId);
            if (member is not null)
            {
                room.Members.Remove(member);
            }

            if (room.Members.Count == 0)
            {
                CancelTurnTimer(roomCode);
                rooms.TryRemove(room.RoomCode, out _);
                return;
            }

            if (room.HostSessionId == sessionId)
            {
                var newHost = room.Members.First();
                room.HostSessionId = newHost.SessionId;
                foreach (var entry in room.Members)
                {
                    entry.IsHost = entry.SessionId == newHost.SessionId;
                }
            }
        }
        finally
        {
            room.SyncLock.Release();
        }

        Notify(room.RoomCode);
    }

    private void StartTurnTimer(string roomCode)
    {
        CancelTurnTimer(roomCode);
        turnTimerFired[roomCode] = false;

        var timer = new System.Threading.Timer(async _ =>
        {
            turnTimerFired[roomCode] = true;
            CancelTurnTimer(roomCode);
            await AutoPlayOnTimeoutAsync(roomCode);
        }, null, TurnTimeoutMs, Timeout.Infinite);

        turnTimers[roomCode] = timer;
        Notify(roomCode);
    }

    private void CancelTurnTimer(string roomCode)
    {
        if (turnTimers.TryRemove(roomCode, out var timer))
        {
            timer.Dispose();
        }
        turnTimerFired.TryRemove(roomCode, out _);
        Notify(roomCode);
    }

    private async Task AutoPlayOnTimeoutAsync(string roomCode)
    {
        var room = GetRoom(roomCode);
        if (room?.Engine is null)
        {
            return;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            if (!room.Engine.CurrentPlayer.IsHuman)
            {
                return;
            }

            if (room.Engine.Phase == GamePhase.Bidding)
            {
                var allowed = room.Engine.GetAllowedBids(room.Engine.CurrentTurnIndex);
                var bid = allowed.Count > 0 ? allowed[0] : 0;
                room.Engine.PlaceBid(bid);
            }
            else if (room.Engine.Phase == GamePhase.TrickPlaying)
            {
                var legal = room.Engine.GetLegalCards(room.Engine.CurrentTurnIndex);
                if (legal.Count > 0)
                {
                    var card = legal.OrderBy(c => c.Rank).First();
                    room.Engine.PlayCard(card);
                }
            }
        }
        finally
        {
            room.SyncLock.Release();
        }

        Notify(room.RoomCode);
        await RunAutomaticFlowAsync(room.RoomCode);
    }

    private async Task RunAutomaticFlowAsync(string roomCode)
    {
        var room = GetRoom(roomCode);
        if (room?.Engine is null)
        {
            return;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            while (room.Engine is not null)
            {
                if (room.Engine.IsTrickResolutionPending)
                {
                    Notify(room.RoomCode);
                    await Task.Delay(RevealWinnerDelayMs);
                    room.Engine.ResolvePendingTrick();
                    Notify(room.RoomCode);
                    await Task.Delay(AfterResolveDelayMs);
                    continue;
                }

                if (room.Engine.Phase is GamePhase.RoundComplete or GamePhase.MatchComplete)
                {
                    break;
                }

                if (room.Engine.CurrentPlayer.IsHuman)
                {
                    break;
                }

                await Task.Delay(room.Engine.Phase == GamePhase.Bidding ? BidDelayMs : PlayDelayMs);
                if (!room.Engine.RunAiTurn())
                {
                    break;
                }

                Notify(room.RoomCode);
            }
        }
        finally
        {
            room.SyncLock.Release();
        }

        if (room.Engine?.CurrentPlayer.IsHuman == true && room.Engine.Phase is GamePhase.Bidding or GamePhase.TrickPlaying && !room.Engine.IsTrickResolutionPending)
        {
            StartTurnTimer(roomCode);
        }

        Notify(room.RoomCode);
    }

    private void Notify(string roomCode) => RoomUpdated?.Invoke(roomCode);

    private static string NormalizePlayerName(string? playerName, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(playerName) ? fallback : playerName.Trim();
        return normalized.Length > 24 ? normalized[..24] : normalized;
    }

    private static string NormalizePlayerId(string? playerId, string fallback)
    {
        return string.IsNullOrWhiteSpace(playerId) ? fallback : playerId.Trim();
    }

    private string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string code;
        do
        {
            code = new string(Enumerable.Range(0, 6).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
        }
        while (rooms.ContainsKey(code));

        return code;
    }
}
