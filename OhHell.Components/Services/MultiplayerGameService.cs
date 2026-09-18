using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OhHell.Core;
using OhHell.Components.Models;

namespace OhHell.Components.Services;

public sealed class MultiplayerGameService : IDisposable
{
    private const int BidDelayMs = 450;
    private const int PlayDelayMs = 500;
    private const int RevealWinnerDelayMs = 2200;
    private const int AfterResolveDelayMs = 180;
    private const int TurnTimeoutMs = 15_000;
    private const int RoomCleanupIntervalMs = 60_000;
    private const int StaleRoomMinutes = 30;

    private readonly ConcurrentDictionary<string, OnlineGameRoom> rooms = new();
    private readonly ConcurrentDictionary<string, ITimer> turnTimers = new();
    private readonly ConcurrentDictionary<string, TurnDeadline> turnDeadlines = new();
    private readonly TimeProvider clock;
    private readonly ILogger<MultiplayerGameService> logger;
    private ITimer? cleanupTimer;

    private sealed record TurnStamp(GameEngine Engine, int Round, int Player, GamePhase Phase, int Bids, int TrickCards, int HandCards)
    {
        public static TurnStamp Capture(GameEngine engine) => new(engine, engine.RoundNumber, engine.CurrentTurnIndex,
            engine.Phase, engine.BidCountThisRound, engine.CurrentTrick.Count, engine.CurrentPlayer.Hand.Count);
    }
    private sealed record TurnDeadline(Guid Token, TurnStamp Stamp, DateTimeOffset ExpiresAt);

    public MultiplayerGameService(TimeProvider? clock = null, ILogger<MultiplayerGameService>? logger = null)
    {
        this.clock = clock ?? TimeProvider.System;
        this.logger = logger ?? NullLogger<MultiplayerGameService>.Instance;
        cleanupTimer = TimeProvider.System.CreateTimer(_ => CleanupStaleRooms(), null,
            TimeSpan.FromMilliseconds(RoomCleanupIntervalMs), TimeSpan.FromMilliseconds(RoomCleanupIntervalMs));
    }

    public DateTimeOffset? GetTurnDeadline(string roomCode) =>
        turnDeadlines.TryGetValue(roomCode.Trim().ToUpperInvariant(), out var deadline) ? deadline.ExpiresAt : null;

    public int GetRemainingTurnTimeMs(string roomCode) => GetTurnDeadline(roomCode) is { } deadline
        ? (int)Math.Clamp((deadline - clock.GetUtcNow()).TotalMilliseconds, 0, TurnTimeoutMs) : 0;

    public event Action<string>? RoomUpdated;
    public event Action<string, EmojiMessage>? EmojiReceived;

    public bool SendEmoji(string roomCode, string sessionId, string emoji, string? sessionToken = null)
    {
        if (!EmojiMessage.IsValidEmoji(emoji)) return false;
        var room = GetRoom(roomCode);
        if (room is null) return false;
        EmojiMessage message;
        room.SyncLock.Wait();
        try
        {
            var member = room.Members.FirstOrDefault(entry => entry.SessionId == sessionId);
            if (!room.Started || room.Engine is null || member?.PlayerIndex is not { } playerIndex) return false;
            if (!ValidateMemberToken(member, sessionToken)) return false;
            var now = clock.GetUtcNow();
            var timestamps = member.EmojiTimestamps;
            timestamps.RemoveAll(t => now - t >= TimeSpan.FromMinutes(1));
            if (timestamps.Count >= 5) return false;
            timestamps.Add(now);
            message = new EmojiMessage(Guid.NewGuid(), playerIndex, member.Name, emoji, now);
        }
        finally { room.SyncLock.Release(); }
        EmojiReceived?.Invoke(room.RoomCode, message);
        return true;
    }

    public OnlineGameRoom CreateRoom(string sessionId, string playerId, string playerName, int maxPlayers, string boardTheme, string cardTheme, string botDifficulty, bool isPublic = true)
    {
        var normalizedName = NormalizePlayerName(playerName, "Host");
        var sessionToken = Guid.NewGuid().ToString("N");
        var room = new OnlineGameRoom
        {
            RoomCode = GenerateRoomCode(),
            HostSessionId = sessionId,
            MaxPlayers = Math.Clamp(maxPlayers, 3, 6),
            BoardTheme = boardTheme,
            CardTheme = cardTheme,
            IsPublic = isPublic,
            BotDifficulty = botDifficulty
        };

        room.Members.Add(new OnlineRoomMember
        {
            SessionId = sessionId,
            SessionToken = sessionToken,
            PlayerId = NormalizePlayerId(playerId, sessionId),
            Name = normalizedName,
            IsHost = true
        });

        rooms[room.RoomCode] = room;
        Notify(room.RoomCode);
        return room;
    }

    public enum JoinRoomResult { Success, RoomNotFound, RoomFull, GameStarted, NameTaken, SessionTokenMismatch }

    public (OnlineGameRoom? Room, JoinRoomResult Result) JoinRoom(string roomCode, string sessionId, string playerId, string playerName, string? sessionToken = null)
    {
        if (!rooms.TryGetValue(roomCode.Trim().ToUpperInvariant(), out var room))
        {
            return (null, JoinRoomResult.RoomNotFound);
        }

        room.SyncLock.Wait();
        try
        {
            var normalizedPlayerId = NormalizePlayerId(playerId, sessionId);
            var existingMember = room.Members.FirstOrDefault(member => member.PlayerId == normalizedPlayerId);
            if (existingMember is not null)
            {
                if (string.IsNullOrEmpty(sessionToken) ||
                    !string.Equals(existingMember.SessionToken, sessionToken, StringComparison.Ordinal))
                {
                    return (null, JoinRoomResult.SessionTokenMismatch);
                }

                if (room.HostSessionId == existingMember.SessionId)
                {
                    room.HostSessionId = sessionId;
                }

                existingMember.SessionId = sessionId;
                existingMember.SessionToken = Guid.NewGuid().ToString("N");
                return (room, JoinRoomResult.Success);
            }

            if (room.Members.Any(member => member.SessionId == sessionId))
            {
                return (room, JoinRoomResult.Success);
            }

            if (room.Started)
            {
                return (null, JoinRoomResult.GameStarted);
            }

            if (room.Members.Count >= room.MaxPlayers)
            {
                return (null, JoinRoomResult.RoomFull);
            }

            var normalizedName = NormalizePlayerName(playerName, $"Player {room.Members.Count + 1}");
            if (room.Members.Any(member => string.Equals(member.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                return (null, JoinRoomResult.NameTaken);
            }

            room.Members.Add(new OnlineRoomMember
            {
                SessionId = sessionId,
                SessionToken = Guid.NewGuid().ToString("N"),
                PlayerId = normalizedPlayerId,
                Name = normalizedName,
                IsHost = false
            });
            Notify(room.RoomCode);
            return (room, JoinRoomResult.Success);
        }
        finally
        {
            room.SyncLock.Release();
        }
    }

    public OnlineGameRoom? GetRoom(string roomCode)
    {
        rooms.TryGetValue(roomCode.Trim().ToUpperInvariant(), out var room);
        return room;
    }

    public bool ValidateSessionToken(string roomCode, string sessionId, string sessionToken)
    {
        if (!rooms.TryGetValue(roomCode.Trim().ToUpperInvariant(), out var room))
        {
            return false;
        }

        room.SyncLock.Wait();
        try
        {
            var member = room.Members.FirstOrDefault(m => m.SessionId == sessionId);
            return member is not null && string.Equals(member.SessionToken, sessionToken, StringComparison.Ordinal);
        }
        finally
        {
            room.SyncLock.Release();
        }
    }

    public IReadOnlyList<OnlineRoomSummary> GetActiveRooms(string? playerId)
    {
        var normalizedPlayerId = string.IsNullOrWhiteSpace(playerId) ? null : playerId.Trim();

        return rooms.Values
            .Where(room =>
            {
                room.SyncLock.Wait();
                try
                {
                    if (room.Started && room.Members.Count == 0) return false;
                    if (!room.IsPublic && normalizedPlayerId is null) return false;
                    if (!room.IsPublic && !room.Members.Any(member => member.PlayerId == normalizedPlayerId)) return false;
                    return true;
                }
                finally
                {
                    room.SyncLock.Release();
                }
            })
            .Select(room =>
            {
                room.SyncLock.Wait();
                try
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
                        IsPublic = room.IsPublic,
                        BoardTheme = room.BoardTheme,
                        CardTheme = room.CardTheme,
                        CanJoin = canJoin,
                        CanRejoin = canRejoin
                    };
                }
                finally
                {
                    room.SyncLock.Release();
                }
            })
            .OrderBy(summary => summary.Started)
            .ThenBy(summary => summary.RoomCode, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<bool> StartGameAsync(string roomCode, string sessionId, string? sessionToken = null)
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

            if (!ValidateMemberToken(member, sessionToken)) return false;

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
                var difficulty = Enum.TryParse<BotDifficulty>(room.BotDifficulty, true, out var parsed) ? parsed : BotDifficulty.Hard;
                definitions.Add(new PlayerDefinition($"Bot {botIndex}", false, difficulty));
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

    public async Task<bool> PlaceBidAsync(string roomCode, string sessionId, int bid, int expectedRevision, string? sessionToken = null)
    {
        var room = GetRoom(roomCode);
        if (room?.Engine is null)
        {
            return false;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            if (room.Engine.Revision != expectedRevision)
            {
                return false;
            }

            var member = room.Members.FirstOrDefault(entry => entry.SessionId == sessionId);
            if (member?.PlayerIndex != room.Engine.CurrentTurnIndex || room.Engine.Phase != GamePhase.Bidding)
            {
                return false;
            }

            if (!ValidateMemberToken(member, sessionToken)) return false;

            if (!room.Engine.GetAllowedBids(room.Engine.CurrentTurnIndex).Contains(bid))
            {
                return false;
            }

            room.Engine.PlaceBid(bid);
            CancelTurnTimer(room.RoomCode);
        }
        finally
        {
            room.SyncLock.Release();
        }

        Notify(room.RoomCode);
        await RunAutomaticFlowAsync(room.RoomCode);
        return true;
    }

    public async Task<bool> PlayCardAsync(string roomCode, string sessionId, Card card, int expectedRevision, string? sessionToken = null)
    {
        var room = GetRoom(roomCode);
        if (room?.Engine is null)
        {
            return false;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            if (room.Engine.Revision != expectedRevision)
            {
                return false;
            }

            var member = room.Members.FirstOrDefault(entry => entry.SessionId == sessionId);
            if (member?.PlayerIndex != room.Engine.CurrentTurnIndex || room.Engine.Phase != GamePhase.TrickPlaying || room.Engine.IsTrickResolutionPending)
            {
                return false;
            }

            if (!ValidateMemberToken(member, sessionToken)) return false;

            if (!room.Engine.GetLegalCards(room.Engine.CurrentTurnIndex).Contains(card))
            {
                return false;
            }

            room.Engine.PlayCard(card);
            CancelTurnTimer(room.RoomCode);
        }
        finally
        {
            room.SyncLock.Release();
        }

        Notify(room.RoomCode);
        await RunAutomaticFlowAsync(room.RoomCode);
        return true;
    }

    public async Task<bool> AdvanceRoundAsync(string roomCode, string sessionId, string? sessionToken = null)
    {
        var room = GetRoom(roomCode);
        if (room?.Engine is null)
        {
            return false;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            var member = room.Members.FirstOrDefault(member => member.SessionId == sessionId);
            if (member is null || room.Engine.Phase != GamePhase.RoundComplete)
            {
                return false;
            }

            if (!ValidateMemberToken(member, sessionToken)) return false;

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

    public async Task LeaveRoomAsync(string roomCode, string sessionId, string? sessionToken = null)
    {
        var room = GetRoom(roomCode);
        if (room is null)
        {
            return;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            var member = room.Members.FirstOrDefault(entry => entry.SessionId == sessionId);
            if (member is null)
            {
                return;
            }
            if (!ValidateMemberToken(member, sessionToken)) return;
            room.Members.Remove(member);

            if (room.Members.Count == 0)
            {
                CancelTurnTimer(room.RoomCode);
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
        var room = GetRoom(roomCode);
        if (room?.Engine is not { } engine || !engine.CurrentPlayer.IsHuman || engine.IsTrickResolutionPending ||
            engine.Phase is not (GamePhase.Bidding or GamePhase.TrickPlaying)) return;

        var stamp = TurnStamp.Capture(engine);
        // Repeated notifications/flow runners must not grant more time to the same turn.
        if (turnDeadlines.TryGetValue(roomCode, out var existing) && existing.Stamp == stamp) return;
        CancelTurnTimer(roomCode);
        var deadline = new TurnDeadline(Guid.NewGuid(), stamp, clock.GetUtcNow().AddMilliseconds(TurnTimeoutMs));
        turnDeadlines[roomCode] = deadline;
        var timer = clock.CreateTimer(_ => { _ = HandleTimeoutAsync(roomCode, deadline); }, null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        turnTimers[roomCode] = timer;
        timer.Change(TimeSpan.FromMilliseconds(TurnTimeoutMs), Timeout.InfiniteTimeSpan);
    }

    private void CancelTurnTimer(string roomCode)
    {
        turnDeadlines.TryRemove(roomCode, out _);
        if (turnTimers.TryRemove(roomCode, out var timer))
        {
            timer.Dispose();
        }
    }

    private async Task HandleTimeoutAsync(string roomCode, TurnDeadline deadline)
    {
        try { await AutoPlayOnTimeoutAsync(roomCode, deadline); }
        catch (Exception exception) { logger.LogError(exception, "Automatic turn failed for room {RoomCode}", roomCode); }
    }

    private async Task AutoPlayOnTimeoutAsync(string roomCode, TurnDeadline deadline)
    {
        var room = GetRoom(roomCode);
        if (room?.Engine is null)
        {
            return;
        }

        await room.SyncLock.WaitAsync();
        try
        {
            if (!turnDeadlines.TryGetValue(roomCode, out var active) || active.Token != deadline.Token ||
                clock.GetUtcNow() < deadline.ExpiresAt || TurnStamp.Capture(room.Engine) != deadline.Stamp ||
                room.Engine.IsTrickResolutionPending || !room.Engine.CurrentPlayer.IsHuman)
            {
                return;
            }
            CancelTurnTimer(roomCode);

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
                    room.SyncLock.Release();
                    try
                    {
                        await Task.Delay(RevealWinnerDelayMs);
                    }
                    finally
                    {
                        await room.SyncLock.WaitAsync();
                    }

                    if (room.Engine is null) break;
                    room.Engine.ResolvePendingTrick();
                    Notify(room.RoomCode);
                    room.SyncLock.Release();
                    try
                    {
                        await Task.Delay(AfterResolveDelayMs);
                    }
                    finally
                    {
                        await room.SyncLock.WaitAsync();
                    }

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

                var delayMs = room.Engine.Phase == GamePhase.Bidding ? BidDelayMs : PlayDelayMs;
                room.SyncLock.Release();
                try
                {
                    await Task.Delay(delayMs);
                }
                finally
                {
                    await room.SyncLock.WaitAsync();
                }

                if (room.Engine is null) break;
                if (!room.Engine.RunAiTurn())
                {
                    break;
                }

                Notify(room.RoomCode);
            }

            if (room.Engine?.CurrentPlayer.IsHuman == true && room.Engine.Phase is GamePhase.Bidding or GamePhase.TrickPlaying && !room.Engine.IsTrickResolutionPending)
            {
                StartTurnTimer(room.RoomCode);
            }
        }
        finally
        {
            room.SyncLock.Release();
        }

        Notify(room.RoomCode);
    }

    public void NotifySessionDisconnected(string sessionId)
    {
        foreach (var roomCode in rooms.Keys.ToList())
        {
            if (!rooms.TryGetValue(roomCode, out var room)) continue;
            room.SyncLock.Wait();
            try
            {
                var member = room.Members.FirstOrDefault(m => m.SessionId == sessionId);
                if (member is null) continue;
                room.Members.Remove(member);
                if (room.Members.Count == 0)
                {
                    CancelTurnTimer(room.RoomCode);
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
    }

    private void CleanupStaleRooms()
    {
        var now = clock.GetUtcNow();
        foreach (var roomCode in rooms.Keys.ToList())
        {
            if (!rooms.TryGetValue(roomCode, out var room)) continue;

            room.SyncLock.Wait();
            try
            {
                if (room.Started) continue;
                if (now - room.CreatedAt > TimeSpan.FromMinutes(StaleRoomMinutes))
                {
                    CancelTurnTimer(room.RoomCode);
                    rooms.TryRemove(room.RoomCode, out _);
                }
            }
            finally
            {
                room.SyncLock.Release();
            }
        }
    }

    public void Dispose()
    {
        cleanupTimer?.Dispose();
        foreach (var code in turnTimers.Keys) CancelTurnTimer(code);
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

    private static bool ValidateMemberToken(OnlineRoomMember member, string? sessionToken)
    {
        if (string.IsNullOrEmpty(sessionToken)) return true;
        return string.Equals(member.SessionToken, sessionToken, StringComparison.Ordinal);
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
