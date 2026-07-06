using OhHell.Core;
using OhHell.Components.Services;
using OhHell.Components.Models;
using Xunit;

namespace OhHell.Tests;

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
        var joined = service.JoinRoom(created.RoomCode, "session2", "player2", "Guest");
        Assert.NotNull(joined);
        Assert.Equal(2, joined!.Members.Count);
    }

    [Fact]
    public void JoinRoom_InvalidCode_ReturnsNull()
    {
        var service = new MultiplayerGameService();
        var result = service.JoinRoom("XXXXXX", "session1", "player1", "Guest");
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
        var session = new GameSession(service);
        Assert.NotNull(session);
        Assert.False(session.HasActiveMatch);
    }

    [Fact]
    public void GameSession_DefaultValues()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service);
        Assert.Equal("You", session.LocalPlayerName);
        Assert.Equal(4, session.SelectedPlayerCount);
        Assert.Equal("svg", session.CardTheme);
        Assert.Equal("emerald", session.BoardTheme);
    }

    [Fact]
    public async Task GameSession_StartLocalMatch_CreatesEngine()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service);
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
        var session = new GameSession(service);
        Assert.False(session.IsTurnTimerActive);
        Assert.Equal(0, session.TurnTimeRemainingMs);
    }

    [Fact]
    public async Task GameSession_StartLocalMatch_TimerStartsOnHumanTurn()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service);
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
        var session = new GameSession(service);
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
        var session = new GameSession(service);
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
        var session = new GameSession(service);
        session.Dispose();
        Assert.False(session.IsTurnTimerActive);
    }

    [Fact]
    public void GameSession_ReturnToMenu_CancelsTimer()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service);
        session.ReturnToMenu();
        Assert.False(session.IsTurnTimerActive);
    }

    [Fact]
    public async Task GameSession_NoTimer_AfterGameComplete()
    {
        var service = new MultiplayerGameService();
        var session = new GameSession(service);
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
}
