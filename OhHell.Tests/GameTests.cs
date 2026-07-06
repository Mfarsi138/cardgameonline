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
