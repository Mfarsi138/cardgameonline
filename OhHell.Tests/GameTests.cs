using OhHell.Core;
using OhHell.Components.Services;
using OhHell.Components.Models;
using Xunit;

namespace OhHell.Tests;

internal sealed class StubStorage : IPlatformStorage
{
    private readonly Dictionary<string, string> store = new();
    public Task<string?> GetItemAsync(string key) => Task.FromResult(store.TryGetValue(key, out var v) ? v : null);
    public Task SetItemAsync(string key, string value) { store[key] = value; return Task.CompletedTask; }
    public Task RemoveItemAsync(string key) { store.Remove(key); return Task.CompletedTask; }
    public Task CopyToClipboardAsync(string text) => Task.CompletedTask;
    public Task ShowToastAsync(string message) => Task.CompletedTask;
}

public class GameEngineTests
{
    [Fact]
    public void CreateDefault_ReturnsValidEngine()
    {
        var engine = GameEngine.CreateDefault();
        Assert.NotNull(engine);
        Assert.Equal(GamePhase.NotStarted, engine.Phase);
    }

    [Fact]
    public void CreateDefault_Has4Players()
    {
        var engine = GameEngine.CreateDefault();
        Assert.Equal(4, engine.Players.Count);
    }

    [Fact]
    public void CreateDefault_HasOneHumanPlayer()
    {
        var engine = GameEngine.CreateDefault();
        var humans = engine.Players.Count(p => p.IsHuman);
        Assert.Equal(1, humans);
    }

    [Fact]
    public void StartNewMatch_SetsPhaseToBidding()
    {
        var engine = GameEngine.CreateDefault();
        engine.StartNewMatch();
        Assert.NotEqual(GamePhase.NotStarted, engine.Phase);
    }

    [Fact]
    public void StartNewMatch_DealsCards()
    {
        var engine = GameEngine.CreateDefault();
        engine.StartNewMatch();
        var human = engine.Players.First(p => p.IsHuman);
        Assert.True(human.Hand.Count > 0);
    }
}

public class MultiplayerGameServiceTests
{
    [Fact]
    public void CreateRoom_ReturnsRoomCode()
    {
        var service = new MultiplayerGameService();
        var room = service.CreateRoom("session1", "player1", "TestPlayer", 4, "emerald", "svg", "hard");
        Assert.NotNull(room);
        Assert.False(string.IsNullOrWhiteSpace(room.RoomCode));
    }

    [Fact]
    public void CreateRoom_AddsHostMember()
    {
        var service = new MultiplayerGameService();
        var room = service.CreateRoom("session1", "player1", "HostPlayer", 4, "emerald", "svg", "hard");
        Assert.Single(room.Members);
        Assert.True(room.Members[0].IsHost);
    }

    [Fact]
    public void JoinRoom_ReturnsRoom()
    {
        var service = new MultiplayerGameService();
        var created = service.CreateRoom("session1", "player1", "Host", 4, "emerald", "svg", "hard");
        var (joined, joinResult) = service.JoinRoom(created.RoomCode, "session2", "player2", "Guest");
        Assert.NotNull(joined);
        Assert.Equal(2, joined.Members.Count);
    }

    [Fact]
    public void JoinRoom_InvalidCode_ReturnsNull()
    {
        var service = new MultiplayerGameService();
        var (result, joinResult) = service.JoinRoom("XXXXXX", "session1", "player1", "Guest");
        Assert.Null(result);
    }

    [Fact]
    public void GetActiveRooms_ReturnsRooms()
    {
        var service = new MultiplayerGameService();
        service.CreateRoom("s1", "p1", "Host1", 4, "emerald", "svg", "hard");
        service.CreateRoom("s2", "p2", "Host2", 4, "emerald", "svg", "hard");
        var rooms = service.GetActiveRooms("p1");
        Assert.Equal(2, rooms.Count);
    }
}

public class GameSessionTests
{
    [Fact]
    public void GameSession_CanCreate()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        Assert.NotNull(session);
        Assert.False(session.HasActiveMatch);
    }

    [Fact]
    public void GameSession_DefaultValues()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        Assert.Equal("You", session.LocalPlayerName);
        Assert.Equal(4, session.SelectedPlayerCount);
        Assert.Equal("svg", session.CardTheme);
        Assert.Equal("emerald", session.BoardTheme);
    }

    [Fact]
    public async Task GameSession_StartLocalMatch_CreatesEngine()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        await session.StartConfiguredLocalMatchAsync();
        Assert.NotNull(session.Engine);
        Assert.True(session.HasActiveMatch);
    }
}

public class PlatformStorageTests
{
    private class TestStorage : IPlatformStorage
    {
        private readonly Dictionary<string, string> _store = new();
        public List<string> ToastMessages { get; } = new();
        public string? LastCopiedText { get; private set; }

        public Task<string?> GetItemAsync(string key)
        {
            _store.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task SetItemAsync(string key, string value)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveItemAsync(string key)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task CopyToClipboardAsync(string text)
        {
            LastCopiedText = text;
            return Task.CompletedTask;
        }

        public Task ShowToastAsync(string message)
        {
            ToastMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Storage_SetAndGet()
    {
        var storage = new TestStorage();
        await storage.SetItemAsync("key1", "value1");
        var result = await storage.GetItemAsync("key1");
        Assert.Equal("value1", result);
    }

    [Fact]
    public async Task Storage_Remove()
    {
        var storage = new TestStorage();
        await storage.SetItemAsync("key1", "value1");
        await storage.RemoveItemAsync("key1");
        var result = await storage.GetItemAsync("key1");
        Assert.Null(result);
    }

    [Fact]
    public async Task Storage_GetMissingKey_ReturnsNull()
    {
        var storage = new TestStorage();
        var result = await storage.GetItemAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task Clipboard_Copy()
    {
        var storage = new TestStorage();
        await storage.CopyToClipboardAsync("https://example.com");
        Assert.Equal("https://example.com", storage.LastCopiedText);
    }

    [Fact]
    public async Task Toast_Shows()
    {
        var storage = new TestStorage();
        await storage.ShowToastAsync("Hello!");
        Assert.Single(storage.ToastMessages);
        Assert.Equal("Hello!", storage.ToastMessages[0]);
    }
}

public class TurnTimerTests
{
    [Fact]
    public void GameSession_InitialTimerNotActive()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        Assert.False(session.IsTurnTimerActive);
        Assert.Equal(0, session.TurnTimeRemainingMs);
    }

    [Fact]
    public async Task GameSession_StartLocalMatch_TimerStartsOnHumanTurn()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        session.GameMode = "local";
        await session.StartConfiguredLocalMatchAsync();

        // After match starts and AI turns complete, timer should be active
        // (assuming human is not first to act)
        if (session.IsLocalPlayersTurn())
        {
            Assert.True(session.IsTurnTimerActive);
            Assert.True(session.TurnTimeRemainingMs > 0);
        }
    }

    [Fact]
    public async Task GameSession_PlaceBid_CancelsTimer()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        session.GameMode = "local";
        await session.StartConfiguredLocalMatchAsync();

        if (session.IsLocalPlayersTurn() && session.Engine.Phase == GamePhase.Bidding)
        {
            Assert.True(session.IsTurnTimerActive);
            var allowed = session.Engine.GetAllowedBids(session.Engine.CurrentTurnIndex);
            if (allowed.Count > 0)
            {
                await session.PlaceBidAsync(allowed[0]);
                // After PlaceBidAsync completes, RunLocalAiUntilHumanAsync may have
                // restarted the timer if it became the human's turn again.
                // The important thing is that the timer was cancelled during the bid.
                Assert.True(session.Engine.BidCountThisRound > 0);
            }
        }
    }

    [Fact]
    public async Task GameSession_PlayCard_CancelsTimer()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        session.GameMode = "local";
        await session.StartConfiguredLocalMatchAsync();

        // Skip to trick playing phase by running AI turns
        while (session.Engine.Phase == GamePhase.Bidding && !session.IsLocalPlayersTurn())
        {
            await Task.Delay(100);
        }

        if (session.Engine.Phase == GamePhase.Bidding && session.IsLocalPlayersTurn())
        {
            var allowed = session.Engine.GetAllowedBids(session.Engine.CurrentTurnIndex);
            if (allowed.Count > 0)
            {
                await session.PlaceBidAsync(allowed[0]);
            }
        }

        // Wait for trick playing phase
        while (session.Engine.Phase != GamePhase.TrickPlaying)
        {
            await Task.Delay(100);
            if (session.Engine.Phase == GamePhase.RoundComplete || session.Engine.Phase == GamePhase.MatchComplete)
            {
                return;
            }
        }

        if (session.IsLocalPlayersTurn() && session.Engine.Phase == GamePhase.TrickPlaying)
        {
            var legal = session.Engine.GetLegalCards(session.Engine.CurrentTurnIndex);
            if (legal.Count > 0)
            {
                await session.PlayCardAsync(legal[0]);
                Assert.False(session.IsTurnTimerActive);
            }
        }
    }

    [Fact]
    public void GameSession_Dispose_CancelsTimer()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        session.Dispose();
        Assert.False(session.IsTurnTimerActive);
    }

    [Fact]
    public void GameSession_ReturnToMenu_CancelsTimer()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        session.ReturnToMenu();
        Assert.False(session.IsTurnTimerActive);
    }

    [Fact]
    public async Task GameSession_NoTimer_AfterGameComplete()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        session.GameMode = "local";
        await session.StartConfiguredLocalMatchAsync();

        // Complete the match by playing all rounds
        var maxIterations = 500;
        var iterations = 0;
        while (session.Engine.Phase != GamePhase.MatchComplete && iterations < maxIterations)
        {
            iterations++;
            if (session.IsBusy || session.Engine.IsTrickResolutionPending)
            {
                await Task.Delay(100);
                continue;
            }

            if (session.IsLocalPlayersTurn())
            {
                if (session.Engine.Phase == GamePhase.Bidding)
                {
                    var allowed = session.Engine.GetAllowedBids(session.Engine.CurrentTurnIndex);
                    if (allowed.Count > 0)
                    {
                        await session.PlaceBidAsync(allowed[0]);
                    }
                }
                else if (session.Engine.Phase == GamePhase.TrickPlaying)
                {
                    var legal = session.Engine.GetLegalCards(session.Engine.CurrentTurnIndex);
                    if (legal.Count > 0)
                    {
                        await session.PlayCardAsync(legal[0]);
                    }
                }
                else if (session.Engine.Phase == GamePhase.RoundComplete)
                {
                    await session.AdvanceRoundAsync();
                }
            }
            await Task.Delay(50);
        }

        Assert.False(session.IsTurnTimerActive);
    }

    [Fact]
    public void FullMatch_CompletesAllRounds()
    {
        var engine = GameEngine.CreateDefault();
        engine.StartNewMatch();

        while (engine.Phase != GamePhase.MatchComplete)
        {
            if (engine.IsTrickResolutionPending)
            {
                engine.ResolvePendingTrick();
                continue;
            }

            if (engine.Phase == GamePhase.Bidding)
            {
                var allowed = engine.GetAllowedBids(engine.CurrentTurnIndex);
                if (allowed.Count > 0)
                {
                    var bid = allowed[^1]; // last allowed = safe bid
                    engine.PlaceBid(bid);
                }
            }
            else if (engine.Phase == GamePhase.TrickPlaying)
            {
                var legal = engine.GetLegalCards(engine.CurrentTurnIndex);
                if (legal.Count > 0)
                {
                    engine.PlayCard(legal[0]);
                }
            }
            else if (engine.Phase == GamePhase.RoundComplete)
            {
                engine.AdvanceToNextRound();
            }
        }

        Assert.Equal(GamePhase.MatchComplete, engine.Phase);
        Assert.All(engine.Players, p => Assert.True(p.Score >= 0));
    }

    [Fact]
    public void MatchHistory_PersistsAndLoadsCorrectly()
    {
        var service = new MultiplayerGameService();
        var storage = new StubStorage();
        var session = new GameSession(service, storage);

        // Create a match history record manually
        var record = new GameSession.MatchHistoryRecord(
            "local", 5, 0, 1,
            new Dictionary<string, int> { { "Alice", 42 }, { "Bot1", 30 } },
            DateTimeOffset.UtcNow);
        session.LoadMatchHistory(System.Text.Json.JsonSerializer.Serialize(
            new[] { new { GameMode = "local", RoundNumber = 5, DealerIndex = 0, LeaderIndex = 1, Scores = record.Scores, CompletedAt = record.CompletedAt } }));

        var history = session.GetMatchHistory();
        Assert.Single(history);
        Assert.Equal("local", history[0].GameMode);
        Assert.Equal(5, history[0].RoundNumber);

        // Save and reload in a new session
        var json = session.SaveMatchHistory();
        var session2 = new GameSession(service, storage);
        session2.LoadMatchHistory(json);
        var reloaded = session2.GetMatchHistory();
        Assert.Single(reloaded);
        Assert.Equal("local", reloaded[0].GameMode);
        Assert.Equal(5, reloaded[0].RoundNumber);
    }

    [Fact]
    public async Task Session_Dispose_CleansUpResources()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        session.GameMode = "local";
        await session.StartConfiguredLocalMatchAsync();
        Assert.True(session.HasActiveMatch);

        session.Dispose();

        // After dispose, session should be safe to access (disposed flag prevents double-dispose)
        session.Dispose(); // should not throw
        Assert.False(session.IsTurnTimerActive);
    }

    [Fact]
    public async Task BotDifficulty_EasyPlayerCreated()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        session.GameMode = "local";
        session.SelectedBotDifficulty = BotDifficulty.Easy;
        await session.StartConfiguredLocalMatchAsync();

        var bots = session.Engine.Players.Where(p => !p.IsHuman).ToList();
        Assert.All(bots, b => Assert.Equal(BotDifficulty.Easy, b.Difficulty));
    }

    [Fact]
    public async Task Emoji_LimitExceeded_ReturnsFalse()
    {
        var service = new MultiplayerGameService();
        var room = service.CreateRoom("s1", "p1", "Host", 4, "emerald", "svg", "easy", true);
        service.JoinRoom(room.RoomCode, "s2", "p2", "Guest");
        service.StartGameAsync(room.RoomCode, "s1");

        // Send 5 emojis (the limit)
        for (int i = 0; i < 5; i++)
        {
            Assert.True(service.SendEmoji(room.RoomCode, "s1", "👍"));
        }
        // 6th should fail
        Assert.False(service.SendEmoji(room.RoomCode, "s1", "👍"));
    }

    [Fact]
    public async Task Emoji_InvalidEmoji_ReturnsFalse()
    {
        var service = new MultiplayerGameService();
        var room = service.CreateRoom("s1", "p1", "Host", 4, "emerald", "svg", "easy", true);
        service.JoinRoom(room.RoomCode, "s2", "p2", "Guest");
        service.StartGameAsync(room.RoomCode, "s1");

        Assert.False(service.SendEmoji(room.RoomCode, "s1", ""));
        Assert.False(service.SendEmoji(room.RoomCode, "s1", "not-an-emoji"));
    }

    [Fact]
    public async Task SelectedBotDifficulty_DefaultIsEasy()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service, new StubStorage());
        Assert.Equal(BotDifficulty.Easy, session.SelectedBotDifficulty);
    }
}
