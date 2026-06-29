using OhHell.Core;
using OhHell.Web.Models;

namespace OhHell.Web.Services;

public sealed class GameSession(MultiplayerGameService multiplayerGameService) : IDisposable
{
    private const int DealStepDelayMs = 70;
    private const int RevealWinnerDelayMs = 2200;
    private const int AfterResolveDelayMs = 180;

    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private GameEngine localEngine = GameEngine.CreateDefault();
    private int lastSeenRoundNumber;
    private int lastSeenCardsPerPlayer;
    private bool lifetimeScoresAppliedForMatch;

    public event Action? StateChanged;

    public GameEngine Engine => CurrentRoom?.Engine ?? localEngine;
    public PlayerState? HumanPlayer => LocalPlayerIndex.HasValue && LocalPlayerIndex.Value < Engine.Players.Count
        ? Engine.Players[LocalPlayerIndex.Value]
        : Engine.Players.FirstOrDefault(player => player.IsHuman);

    public bool HasActiveMatch { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsDealing { get; private set; }
    public int DealStep { get; private set; }
    public bool IsWinnerPauseActive => Engine.IsTrickResolutionPending;
    public bool ShowNextRoundPopup => Engine.Phase == GamePhase.RoundComplete && HasActiveMatch;
    public string LocalPlayerName { get; set; } = "You";
    public int SelectedPlayerCount { get; set; } = 4;
    public int SelectedBotCount { get; set; } = 3;
    public string BoardTheme { get; set; } = "emerald";
    public string CardTheme { get; set; } = "svg";
    public string GameMode { get; set; } = "online";
    public string BrowserPlayerId { get; private set; } = string.Empty;
    public string MultiplayerName { get; set; } = string.Empty;
    public string RoomCodeInput { get; set; } = string.Empty;
    public string? CurrentRoomCode { get; private set; }
    public OnlineGameRoom? CurrentRoom { get; private set; }
    public IReadOnlyList<OnlineRoomSummary> AvailableRooms { get; private set; } = [];
    public bool IsInOnlineRoom => CurrentRoom is not null;
    public bool IsRoomHost => CurrentRoom is not null && CurrentRoom.HostSessionId == sessionId;
    public bool IsOnlineGameStarted => CurrentRoom?.Started == true && CurrentRoom.Engine is not null;
    public int? LocalPlayerIndex => CurrentRoom?.Members.FirstOrDefault(member => member.SessionId == sessionId)?.PlayerIndex ?? (CurrentRoom is null ? 0 : null);
    public string MultiplayerStatusMessage { get; set; } = string.Empty;
    public Dictionary<string, int> LifetimeScores { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public Task EnsureStartedAsync()
    {
        multiplayerGameService.RoomUpdated += HandleRoomUpdated;
        RefreshAvailableRooms();
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public async Task StartConfiguredLocalMatchAsync()
    {
        GameMode = "local";
        var totalPlayers = Math.Clamp(SelectedPlayerCount, 3, 6);
        var definitions = new List<PlayerDefinition> { new(LocalPlayerName, true) };
        for (var index = 1; index < totalPlayers; index++)
        {
            definitions.Add(new PlayerDefinition($"Bot {index}", false));
        }

        localEngine = GameEngine.CreateConfigured(definitions);
        localEngine.StartNewMatch();
        lastSeenRoundNumber = localEngine.RoundNumber;
        lastSeenCardsPerPlayer = localEngine.CardsPerPlayer;
        CurrentRoom = null;
        CurrentRoomCode = null;
        HasActiveMatch = true;
        lifetimeScoresAppliedForMatch = false;
        await RunDealAnimationAsync();
        await RunLocalAiUntilHumanAsync();
    }

    public async Task<bool> CreateOnlineRoomAsync()
    {
        EnsureBrowserPlayerId();
        var room = multiplayerGameService.CreateRoom(sessionId, BrowserPlayerId, MultiplayerName, SelectedPlayerCount, BoardTheme, CardTheme, "hard");
        CurrentRoom = room;
        CurrentRoomCode = room.RoomCode;
        RoomCodeInput = room.RoomCode;
        GameMode = "online";
        HasActiveMatch = false;
        MultiplayerStatusMessage = $"Room {room.RoomCode} created.";
        RefreshAvailableRooms();
        NotifyStateChanged();
        return await Task.FromResult(true);
    }

    public async Task<bool> JoinOnlineRoomAsync()
    {
        EnsureBrowserPlayerId();
        var normalizedRoomCode = RoomCodeInput.Trim().ToUpperInvariant();
        var normalizedName = MultiplayerName.Trim();
        var existingRoom = multiplayerGameService.GetRoom(normalizedRoomCode);
        if (existingRoom is not null &&
            existingRoom.Members.Any(member =>
                member.PlayerId != BrowserPlayerId &&
                string.Equals(member.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            MultiplayerStatusMessage = "That name is already taken in this room. Choose another name.";
            NotifyStateChanged();
            return false;
        }

        var room = multiplayerGameService.JoinRoom(RoomCodeInput, sessionId, BrowserPlayerId, MultiplayerName);
        if (room is null)
        {
            MultiplayerStatusMessage = "Unable to join room. Check code, room capacity, or choose a different name.";
            NotifyStateChanged();
            return false;
        }

        CurrentRoom = room;
        CurrentRoomCode = room.RoomCode;
        BoardTheme = room.BoardTheme;
        CardTheme = room.CardTheme;
        GameMode = "online";
        MultiplayerStatusMessage = $"Joined room {room.RoomCode}.";
        RefreshAvailableRooms();
        NotifyStateChanged();
        return await Task.FromResult(true);
    }

    public async Task<bool> TryRestoreOnlineRoomAsync(string? roomCode, string? playerName)
    {
        EnsureBrowserPlayerId();

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            RefreshAvailableRooms();
            NotifyStateChanged();
            return false;
        }

        if (!string.IsNullOrWhiteSpace(playerName))
        {
            MultiplayerName = playerName.Trim();
        }

        RoomCodeInput = roomCode.Trim().ToUpperInvariant();
        var room = multiplayerGameService.JoinRoom(RoomCodeInput, sessionId, BrowserPlayerId, MultiplayerName);
        if (room is null)
        {
            RefreshAvailableRooms();
            NotifyStateChanged();
            return false;
        }

        CurrentRoom = room;
        CurrentRoomCode = room.RoomCode;
        BoardTheme = room.BoardTheme;
        CardTheme = room.CardTheme;
        GameMode = "online";
        HasActiveMatch = room.Started && room.Engine is not null;
        if (HasActiveMatch)
        {
            lastSeenRoundNumber = Engine.RoundNumber;
            lastSeenCardsPerPlayer = Engine.CardsPerPlayer;
        }

        MultiplayerStatusMessage = HasActiveMatch
            ? $"Rejoined room {room.RoomCode}."
            : $"Returned to room {room.RoomCode}.";
        RefreshAvailableRooms();
        NotifyStateChanged();
        return await Task.FromResult(true);
    }

    public async Task<bool> StartOnlineGameAsync()
    {
        if (CurrentRoomCode is null)
        {
            return false;
        }

        var started = await multiplayerGameService.StartGameAsync(CurrentRoomCode, sessionId);
        if (started)
        {
            await RefreshRoomAsync();
            HasActiveMatch = true;
            lifetimeScoresAppliedForMatch = false;
            lastSeenRoundNumber = Engine.RoundNumber;
            lastSeenCardsPerPlayer = Engine.CardsPerPlayer;
            await RunDealAnimationAsync();
        }

        return started;
    }

    public async Task StartNewMatchAsync()
    {
        if (GameMode == "online" && IsInOnlineRoom)
        {
            await StartOnlineGameAsync();
            return;
        }

        await StartConfiguredLocalMatchAsync();
    }

    public async Task AdvanceRoundAsync()
    {
        if (GameMode == "online" && CurrentRoomCode is not null)
        {
            await multiplayerGameService.AdvanceRoundAsync(CurrentRoomCode, sessionId);
            await RefreshRoomAsync();
            return;
        }

        if (localEngine.Phase == GamePhase.RoundComplete)
        {
            localEngine.AdvanceToNextRound();
            lastSeenRoundNumber = localEngine.RoundNumber;
            lastSeenCardsPerPlayer = localEngine.CardsPerPlayer;
            await RunDealAnimationAsync();
            await RunLocalAiUntilHumanAsync();
        }
    }

    public async Task LeaveOnlineRoomAsync()
    {
        if (CurrentRoomCode is not null)
        {
            await multiplayerGameService.LeaveRoomAsync(CurrentRoomCode, sessionId);
        }

        CurrentRoom = null;
        CurrentRoomCode = null;
        HasActiveMatch = false;
        MultiplayerStatusMessage = string.Empty;
        RefreshAvailableRooms();
        NotifyStateChanged();
    }

    public void ReturnToMenu()
    {
        if (GameMode == "online")
        {
            _ = LeaveOnlineRoomAsync();
        }

        HasActiveMatch = false;
        IsBusy = false;
        IsDealing = false;
        RefreshAvailableRooms();
        NotifyStateChanged();
    }

    public void SetBrowserPlayerId(string? browserPlayerId)
    {
        BrowserPlayerId = string.IsNullOrWhiteSpace(browserPlayerId) ? sessionId : browserPlayerId.Trim();
        RefreshAvailableRooms();
    }

    public async Task PlaceBidAsync(int bid)
    {
        if (IsBusy)
        {
            return;
        }

        if (GameMode == "online" && CurrentRoomCode is not null)
        {
            await multiplayerGameService.PlaceBidAsync(CurrentRoomCode, sessionId, bid);
            await RefreshRoomAsync();
            return;
        }

        if (localEngine.Phase == GamePhase.Bidding && localEngine.CurrentPlayer.IsHuman)
        {
            localEngine.PlaceBid(bid);
            NotifyStateChanged();
            await RunLocalAiUntilHumanAsync();
        }
    }

    public async Task PlayCardAsync(Card card)
    {
        if (IsBusy)
        {
            return;
        }

        if (GameMode == "online" && CurrentRoomCode is not null)
        {
            await multiplayerGameService.PlayCardAsync(CurrentRoomCode, sessionId, card);
            await RefreshRoomAsync();
            return;
        }

        if (localEngine.Phase == GamePhase.TrickPlaying && localEngine.CurrentPlayer.IsHuman)
        {
            localEngine.PlayCard(card);
            NotifyStateChanged();
            await RunLocalAiUntilHumanAsync();
        }
    }

    public int GetAnimatedHandCount(int playerIndex)
    {
        var actualCount = playerIndex >= 0 && playerIndex < Engine.Players.Count ? Engine.Players[playerIndex].Hand.Count : 0;
        if (!IsDealing)
        {
            return actualCount;
        }

        var fullRounds = DealStep / Engine.Players.Count;
        var partialCards = DealStep % Engine.Players.Count;
        var visible = fullRounds + (playerIndex < partialCards ? 1 : 0);
        return Math.Min(actualCount, visible);
    }

    public bool IsLocalPlayersTurn()
    {
        return LocalPlayerIndex.HasValue && Engine.CurrentTurnIndex == LocalPlayerIndex.Value && Engine.CurrentPlayer.IsHuman;
    }

    public bool RecordCompletedMatchIfNeeded()
    {
        if (!HasActiveMatch || Engine.Phase != GamePhase.MatchComplete || lifetimeScoresAppliedForMatch)
        {
            return false;
        }

        foreach (var player in Engine.Players)
        {
            LifetimeScores[player.Name] = LifetimeScores.GetValueOrDefault(player.Name) + player.Score;
        }

        lifetimeScoresAppliedForMatch = true;
        return true;
    }

    public void SetLifetimeScores(Dictionary<string, int>? scores)
    {
        LifetimeScores = scores is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(scores, StringComparer.OrdinalIgnoreCase);
        NotifyStateChanged();
    }

    public void ResetLifetimeScores()
    {
        LifetimeScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        NotifyStateChanged();
    }

    private async Task RunLocalAiUntilHumanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NotifyStateChanged();
        try
        {
            while (!localEngine.CurrentPlayer.IsHuman && !localEngine.IsTrickResolutionPending && localEngine.Phase is not GamePhase.RoundComplete and not GamePhase.MatchComplete)
            {
                await Task.Delay(localEngine.Phase == GamePhase.Bidding ? 500 : 650);
                if (!localEngine.RunAiTurn())
                {
                    break;
                }

                NotifyStateChanged();
                await HandleLocalPendingTrickAsync();
            }
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    private async Task HandleLocalPendingTrickAsync()
    {
        if (!localEngine.IsTrickResolutionPending)
        {
            return;
        }

        NotifyStateChanged();
        await Task.Delay(RevealWinnerDelayMs);
        localEngine.ResolvePendingTrick();
        NotifyStateChanged();
        await Task.Delay(AfterResolveDelayMs);
    }

    private async Task RunDealAnimationAsync()
    {
        IsBusy = true;
        IsDealing = true;
        DealStep = 0;
        NotifyStateChanged();

        var totalDeals = Engine.Players.Count * Engine.CardsPerPlayer;
        while (DealStep < totalDeals)
        {
            await Task.Delay(DealStepDelayMs);
            DealStep++;
            NotifyStateChanged();
        }

        await Task.Delay(120);
        IsDealing = false;
        IsBusy = false;
        NotifyStateChanged();
    }

    private async void HandleRoomUpdated(string roomCode)
    {
        RefreshAvailableRooms();

        if (CurrentRoomCode != roomCode)
        {
            NotifyStateChanged();
            return;
        }

        await RefreshRoomAsync();
    }

    private async Task RefreshRoomAsync()
    {
        if (CurrentRoomCode is null)
        {
            return;
        }

        CurrentRoom = multiplayerGameService.GetRoom(CurrentRoomCode);
        if (CurrentRoom is not null)
        {
            BoardTheme = CurrentRoom.BoardTheme;
            CardTheme = CurrentRoom.CardTheme;
        }
        else
        {
            CurrentRoomCode = null;
            MultiplayerStatusMessage = "That room is no longer available.";
        }

        HasActiveMatch = CurrentRoom?.Started == true && CurrentRoom.Engine is not null;
        RefreshAvailableRooms();
        if (HasActiveMatch && (Engine.RoundNumber != lastSeenRoundNumber || Engine.CardsPerPlayer != lastSeenCardsPerPlayer) && !IsDealing)
        {
            lastSeenRoundNumber = Engine.RoundNumber;
            lastSeenCardsPerPlayer = Engine.CardsPerPlayer;
            _ = RunDealAnimationAsync();
        }
        NotifyStateChanged();
        await Task.CompletedTask;
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();

    private void RefreshAvailableRooms()
    {
        AvailableRooms = multiplayerGameService.GetActiveRooms(BrowserPlayerId);
    }

    private void EnsureBrowserPlayerId()
    {
        if (string.IsNullOrWhiteSpace(BrowserPlayerId))
        {
            BrowserPlayerId = sessionId;
        }
    }

    public void Dispose()
    {
        multiplayerGameService.RoomUpdated -= HandleRoomUpdated;
    }
}
