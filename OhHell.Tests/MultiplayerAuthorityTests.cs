using System.Collections.Concurrent;
using System.Reflection;
using OhHell.Components.Models;
using OhHell.Components.Services;
using OhHell.Core;
using Xunit;

namespace OhHell.Tests;

public class MultiplayerAuthorityTests
{
    [Theory]
    [InlineData("outsider", 0)]
    [InlineData("current", -1)]
    public async Task RejectedBidPreservesActiveDeadline(string actor, int bid)
    {
        var (service, room) = CreateStartedRoom();
        var current = room.Members.Single(m => m.PlayerIndex == room.Engine!.CurrentTurnIndex).SessionId;
        StartTimer(service, room.RoomCode);
        var timer = Timers(service)[room.RoomCode];
        try
        {
            Assert.False(await service.PlaceBidAsync(room.RoomCode, actor == "current" ? current : actor, bid, room.Engine!.Revision));
            Assert.Same(timer, Timers(service)[room.RoomCode]);
            Assert.Equal(0, room.Engine!.BidCountThisRound);
        }
        finally { CancelTimer(service, room.RoomCode); }
    }

    [Fact]
    public async Task RejectedCardPreservesActiveDeadline()
    {
        var (service, room) = CreateStartedRoom();
        var engine = room.Engine!;
        while (engine.Phase == GamePhase.Bidding) engine.PlaceBid(engine.GetAllowedBids(engine.CurrentTurnIndex)[0]);
        StartTimer(service, room.RoomCode);
        var timer = Timers(service)[room.RoomCode];
        try
        {
            Assert.False(await service.PlayCardAsync(room.RoomCode, "outsider", engine.CurrentPlayer.Hand[0], engine.Revision));
            Assert.Same(timer, Timers(service)[room.RoomCode]);
            Assert.Empty(engine.CurrentTrick);
        }
        finally { CancelTimer(service, room.RoomCode); }
    }

    [Fact]
    public async Task OutsiderCannotAdvanceCompletedRound()
    {
        var (service, room) = CreateStartedRoom();
        var engine = room.Engine!;
        for (var action = 0; action < 200 && engine.Phase != GamePhase.RoundComplete; action++)
        {
            if (engine.IsTrickResolutionPending) engine.ResolvePendingTrick();
            else if (engine.Phase == GamePhase.Bidding) engine.PlaceBid(engine.GetAllowedBids(engine.CurrentTurnIndex)[0]);
            else engine.PlayCard(engine.GetLegalCards(engine.CurrentTurnIndex)[0]);
        }
        Assert.Equal(GamePhase.RoundComplete, engine.Phase);
        var round = engine.RoundNumber;
        Assert.False(await service.AdvanceRoundAsync(room.RoomCode, "outsider"));
        Assert.Equal(round, engine.RoundNumber);
        Assert.Equal(GamePhase.RoundComplete, engine.Phase);
    }

    [Fact]
    public async Task OldTimeoutCannotPlayTheFollowingTurn()
    {
        var clock = new ManualClock();
        var (service, room) = CreateStartedRoom(clock);
        using (service)
        {
            StartTimer(service, room.RoomCode);
            var oldTimer = clock.Timers.Single();
            var current = room.Members.Single(m => m.PlayerIndex == room.Engine!.CurrentTurnIndex).SessionId;
            Assert.True(await service.PlaceBidAsync(room.RoomCode, current, 0, room.Engine!.Revision));
            var deadline = service.GetTurnDeadline(room.RoomCode);
            clock.Advance(TimeSpan.FromSeconds(16));
            oldTimer.FireQueuedCallback();
            Assert.Equal(1, room.Engine!.BidCountThisRound);
            Assert.Equal(deadline, service.GetTurnDeadline(room.RoomCode));
        }
    }

    [Fact]
    public void CurrentTimeoutPlaysExactlyOnceEvenIfCallbackIsDeliveredTwice()
    {
        var clock = new ManualClock();
        var (service, room) = CreateStartedRoom(clock);
        using (service)
        {
            StartTimer(service, room.RoomCode);
            var timer = clock.Timers.Single();
            clock.Advance(TimeSpan.FromSeconds(15));
            timer.FireQueuedCallback();
            timer.FireQueuedCallback();
            Assert.Equal(1, room.Engine!.BidCountThisRound);
            Assert.Equal(clock.GetUtcNow().AddSeconds(15), service.GetTurnDeadline(room.RoomCode));
        }
    }

    [Fact]
    public void RepeatedFlowDoesNotExtendTheSameTurnDeadline()
    {
        var clock = new ManualClock();
        var (service, room) = CreateStartedRoom(clock);
        using (service)
        {
            StartTimer(service, room.RoomCode);
            var deadline = service.GetTurnDeadline(room.RoomCode);
            clock.Advance(TimeSpan.FromSeconds(5));
            StartTimer(service, room.RoomCode);
            Assert.Equal(deadline, service.GetTurnDeadline(room.RoomCode));
            Assert.Single(clock.Timers);
        }
    }

    [Fact]
    public async Task OutsiderLeavingDoesNotCancelTheDeadline()
    {
        var clock = new ManualClock();
        var (service, room) = CreateStartedRoom(clock);
        using (service)
        {
            StartTimer(service, room.RoomCode);
            var deadline = service.GetTurnDeadline(room.RoomCode);
            await service.LeaveRoomAsync(room.RoomCode.ToLowerInvariant(), "outsider");
            Assert.Equal(deadline, service.GetTurnDeadline(room.RoomCode));
            Assert.Equal(3, room.Members.Count);
        }
    }

    [Fact]
    public void PrivateRoomNotVisibleToNonMembers()
    {
        var service = new MultiplayerGameService();
        var room = service.CreateRoom("s0", "p0", "First", 3, "emerald", "svg", "hard", isPublic: false);
        service.JoinRoom(room.RoomCode, "s1", "p1", "Second");
        var publicRooms = service.GetActiveRooms(null);
        Assert.Empty(publicRooms);
        var memberRooms = service.GetActiveRooms("p0");
        Assert.Single(memberRooms);
        Assert.Equal(room.RoomCode, memberRooms[0].RoomCode);
        Assert.False(memberRooms[0].IsPublic);
    }

    [Fact]
    public void PublicRoomVisibleToAll()
    {
        var service = new MultiplayerGameService();
        var room = service.CreateRoom("s0", "p0", "First", 3, "emerald", "svg", "hard", isPublic: true);
        service.JoinRoom(room.RoomCode, "s1", "p1", "Second");
        var publicRooms = service.GetActiveRooms(null);
        Assert.Single(publicRooms);
        Assert.True(publicRooms[0].IsPublic);
        var otherRooms = service.GetActiveRooms("p_outsider");
        Assert.Single(otherRooms);
    }

    [Fact]
    public void PrivateRoomCannotBeJoinedByCodeIfNotMember()
    {
        var service = new MultiplayerGameService();
        var room = service.CreateRoom("s0", "p0", "First", 3, "emerald", "svg", "hard", isPublic: false);
        var (result, joinResult) = service.JoinRoom(room.RoomCode, "s_outsider", "p_outsider", "NewPlayer");
        Assert.NotNull(result);
        Assert.Single(result.Members, m => m.PlayerId == "p_outsider");
    }

    [Fact]
    public void PrivateRoomHiddenFromPublicListingButJoinableByCode()
    {
        var service = new MultiplayerGameService();
        var room = service.CreateRoom("s0", "p0", "First", 3, "emerald", "svg", "hard", isPublic: false);
        Assert.Empty(service.GetActiveRooms(null));
        Assert.Empty(service.GetActiveRooms("p_outsider"));
        var (joined, joinResult) = service.JoinRoom(room.RoomCode, "s1", "p1", "Second");
        Assert.NotNull(joined);
        Assert.Single(service.GetActiveRooms("p0"));
        Assert.Empty(service.GetActiveRooms(null));
    }

    [Fact]
    public void ValidEmojiCanBeSent()
    {
        var (service, room) = CreateStartedRoom();
        var received = new List<EmojiMessage>();
        service.EmojiReceived += (_, msg) => received.Add(msg);
        Assert.True(service.SendEmoji(room.RoomCode, "s0", "👍"));
        Assert.Single(received);
        Assert.Equal("First", received[0].PlayerName);
        Assert.Equal("👍", received[0].Emoji);
    }

    [Fact]
    public void InvalidEmojiIsRejected()
    {
        var (service, room) = CreateStartedRoom();
        var received = new List<EmojiMessage>();
        service.EmojiReceived += (_, msg) => received.Add(msg);
        Assert.False(service.SendEmoji(room.RoomCode, "s0", "not_emoji"));
        Assert.Empty(received);
    }

    [Fact]
    public void EmojiRateLimitEnforced()
    {
        var (service, room) = CreateStartedRoom();
        for (var i = 0; i < 5; i++) Assert.True(service.SendEmoji(room.RoomCode, "s0", "👍"));
        Assert.False(service.SendEmoji(room.RoomCode, "s0", "👍"));
    }

    [Fact]
    public async Task ConcurrentEmojiSendsShareOneLimitAcrossRoomCodeCasing()
    {
        var (service, room) = CreateStartedRoom();
        using (service)
        {
            var accepted = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
                service.SendEmoji(i % 2 == 0 ? room.RoomCode : room.RoomCode.ToLowerInvariant(), "s0", EmojiMessage.PresetEmojis[0]))));
            Assert.Equal(5, accepted.Count(value => value));
        }
    }

    [Fact]
    public void EmojiCarriesSeatIdentityAndLimitExpiresAtOneMinute()
    {
        var clock = new ManualClock();
        var (service, room) = CreateStartedRoom(clock);
        using (service)
        {
            var messages = new List<EmojiMessage>();
            service.EmojiReceived += (code, message) => { Assert.Equal(room.RoomCode, code); messages.Add(message); };
            Assert.False(service.SendEmoji(room.RoomCode, "outsider", EmojiMessage.PresetEmojis[0]));
            for (var i = 0; i < 5; i++) Assert.True(service.SendEmoji(room.RoomCode, "s0", EmojiMessage.PresetEmojis[0]));
            Assert.False(service.SendEmoji(room.RoomCode, "s0", EmojiMessage.PresetEmojis[0]));
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.True(service.SendEmoji(room.RoomCode.ToLowerInvariant(), "s0", EmojiMessage.PresetEmojis[0]));
            Assert.All(messages, message => Assert.Equal(0, message.PlayerIndex));
            Assert.Equal(6, messages.Select(message => message.Id).Distinct().Count());
        }
    }

    [Fact]
    public async Task StaleRevisionBidIsRejected()
    {
        var (service, room) = CreateStartedRoom();
        var engine = room.Engine!;
        var current = room.Members.Single(m => m.PlayerIndex == engine.CurrentTurnIndex).SessionId;
        var staleRevision = engine.Revision;
        Assert.True(await service.PlaceBidAsync(room.RoomCode, current, 0, staleRevision));
        Assert.False(await service.PlaceBidAsync(room.RoomCode, current, 1, staleRevision));
        Assert.Equal(1, engine.BidCountThisRound);
    }

    [Fact]
    public async Task StaleRevisionPlayIsRejected()
    {
        var (service, room) = CreateStartedRoom();
        var engine = room.Engine!;
        while (engine.Phase == GamePhase.Bidding) engine.PlaceBid(engine.GetAllowedBids(engine.CurrentTurnIndex)[0]);
        var current = room.Members.Single(m => m.PlayerIndex == engine.CurrentTurnIndex).SessionId;
        var card = engine.CurrentPlayer.Hand[0];
        var staleRevision = engine.Revision;
        Assert.True(await service.PlayCardAsync(room.RoomCode, current, card, staleRevision));
        Assert.False(await service.PlayCardAsync(room.RoomCode, current, engine.CurrentPlayer.Hand[0], staleRevision));
        Assert.Single(engine.CurrentTrick);
    }

    [Fact]
    public async Task OutOfTurnBidIsRejected()
    {
        var (service, room) = CreateStartedRoom();
        var engine = room.Engine!;
        var wrongTurn = room.Members.First(m => m.PlayerIndex != engine.CurrentTurnIndex).SessionId;
        Assert.False(await service.PlaceBidAsync(room.RoomCode, wrongTurn, 0, engine.Revision));
        Assert.Equal(0, engine.BidCountThisRound);
    }

    [Fact]
    public async Task OutOfTurnPlayIsRejected()
    {
        var (service, room) = CreateStartedRoom();
        var engine = room.Engine!;
        while (engine.Phase == GamePhase.Bidding) engine.PlaceBid(engine.GetAllowedBids(engine.CurrentTurnIndex)[0]);
        var wrongTurn = room.Members.First(m => m.PlayerIndex != engine.CurrentTurnIndex).SessionId;
        var card = engine.CurrentPlayer.Hand[0];
        Assert.False(await service.PlayCardAsync(room.RoomCode, wrongTurn, card, engine.Revision));
        Assert.Empty(engine.CurrentTrick);
    }

    private static (MultiplayerGameService, OnlineGameRoom) CreateStartedRoom(TimeProvider? clock = null)
    {
        var service = new MultiplayerGameService(clock);
        var room = service.CreateRoom("s0", "p0", "First", 3, "emerald", "svg", "hard");
        service.JoinRoom(room.RoomCode, "s1", "p1", "Second");
        service.JoinRoom(room.RoomCode, "s2", "p2", "Third");
        for (var i = 0; i < room.Members.Count; i++) room.Members[i].PlayerIndex = i;
        room.Engine = new GameEngine(room.Members.Select(m => new PlayerDefinition(m.Name, true)), seed: 42);
        room.Engine.StartNewMatch();
        room.Started = true;
        return (service, room);
    }

    // Observe an already scheduled deadline without making the tests sleep for 15 seconds.
    private static ConcurrentDictionary<string, ITimer> Timers(MultiplayerGameService service) =>
        (ConcurrentDictionary<string, ITimer>)typeof(MultiplayerGameService).GetField("turnTimers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
    private static void StartTimer(MultiplayerGameService service, string code) => typeof(MultiplayerGameService).GetMethod("StartTurnTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [code]);
    private static void CancelTimer(MultiplayerGameService service, string code) => typeof(MultiplayerGameService).GetMethod("CancelTurnTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [code]);

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        public List<ManualTimer> Timers { get; } = [];
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            Timers.Add(timer);
            return timer;
        }
    }
    // Simulate a callback already queued by the runtime, including after timer disposal.
    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public void FireQueuedCallback() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
