using OhHell.Core;
using OhHell.Components.Models;

namespace OhHell.Components.Services;

public sealed class GameSession(MultiplayerGameService multiplayerGameService, IPlatformStorage storage) : IDisposable
{
    private const int DealStepDelayMs = 70;
    private const int RevealWinnerDelayMs = 2200;
    private const int AfterResolveDelayMs = 180;
    private const int TurnTimeoutMs = 15_000;
    private const int MaxMatchHistory = 20;
    private const string MatchHistoryStorageKey = "ohhell.matchHistory";

    private readonly string sessionId = Guid.NewGuid().ToString("N");
    public string SessionToken => sessionToken;
    private string sessionToken = string.Empty;
    private GameEngine localEngine = GameEngine.CreateDefault();
    private int lastSeenRoundNumber;
    private int lastSeenCardsPerPlayer;
    private bool lifetimeScoresAppliedForMatch;
    private System.Threading.Timer? turnTimer;
    private bool turnTimerFired;
    private int localTurnTimeRemainingMs;
    private readonly CancellationTokenSource clockUpdates = new();
    private readonly CancellationTokenSource lifecycleCts = new();
    private bool subscribed;
    private bool disposed;
    private List<MatchHistoryRecord> matchHistory = new();
    private readonly List<EmojiMessage> recentEmojis = new();
    private int refreshSemaphore;

    public sealed record MatchHistoryRecord(string GameMode, int RoundNumber, int DealerIndex, int LeaderIndex, Dictionary<string, int> Scores, DateTimeOffset CompletedAt);

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
    public BotDifficulty SelectedBotDifficulty { get; set; } = BotDifficulty.Easy;
    public string BoardTheme { get; set; } = "emerald";
    public string CardTheme { get; set; } = "svg";
    public string GameMode { get; set; } = "online";
    public bool CreatePublicRoom { get; set; } = true;
    public string BrowserPlayerId { get; private set; } = string.Empty;
    public string MultiplayerName { get; set; } = string.Empty;
    public string RoomCodeInput { get; set; } = string.Empty;
    public string? CurrentRoomCode { get; private set; }
    public OnlineGameRoom? CurrentRoom { get; private set; }
    public IReadOnlyList<OnlineRoomSummary> AvailableRooms { get; private set; } = [];
    public IReadOnlyList<EmojiMessage> RecentEmojis { get { lock (recentEmojis) return recentEmojis.ToArray(); } }
    public bool ReactionsMuted { get; private set; }
    public void ToggleReactionsMuted()
    {
        lock (recentEmojis)
        {
            ReactionsMuted = !ReactionsMuted;
            recentEmojis.Clear();
        }
        NotifyStateChanged();
    }
    public IReadOnlyList<string> PresetEmojis => EmojiMessage.PresetEmojis;
    public bool IsInOnlineRoom => CurrentRoom is not null;
    public bool IsRoomHost => CurrentRoom is not null && CurrentRoom.HostSessionId == sessionId;
    public bool IsOnlineGameStarted => CurrentRoom?.Started == true && CurrentRoom.Engine is not null;
    public int? LocalPlayerIndex => CurrentRoom?.Members.FirstOrDefault(member => member.SessionId == sessionId)?.PlayerIndex ?? (CurrentRoom is null ? 0 : null);
    public string MultiplayerStatusMessage { get; set; } = string.Empty;
    public Dictionary<string, int> LifetimeScores { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public int TurnTimeRemainingMs
    {
        get => CurrentRoomCode is { } code ? multiplayerGameService.GetRemainingTurnTimeMs(code) : localTurnTimeRemainingMs;
        private set => localTurnTimeRemainingMs = value;
    }
    public bool IsTurnTimerActive => CurrentRoomCode is { } code
        ? multiplayerGameService.GetTurnDeadline(code).HasValue : turnTimer is not null;

    public Task EnsureStartedAsync()
    {
        if (!subscribed)
        {
            multiplayerGameService.RoomUpdated += HandleRoomUpdated;
            multiplayerGameService.EmojiReceived += HandleEmojiReceived;
            subscribed = true;
            _ = RunOnlineClockUpdatesAsync(clockUpdates.Token);
        }
        RefreshAvailableRooms();
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    private async Task RunOnlineClockUpdatesAsync(CancellationToken cancellationToken)
    {
        using var ticker = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await ticker.WaitForNextTickAsync(cancellationToken))
            {
                if (CurrentRoomCode is not null && IsTurnTimerActive) NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public async Task StartConfiguredLocalMatchAsync()
    {
        GameMode = "local";
        var totalPlayers = Math.Clamp(SelectedPlayerCount, 3, 6);
        var definitions = new List<PlayerDefinition> { new(LocalPlayerName, true) };
        for (var index = 1; index < totalPlayers; index++)
        {
            definitions.Add(new PlayerDefinition($"Bot {index}", false, SelectedBotDifficulty));
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
        var room = multiplayerGameService.CreateRoom(sessionId, BrowserPlayerId, MultiplayerName, SelectedPlayerCount, BoardTheme, CardTheme, "hard", CreatePublicRoom);
        CurrentRoom = room;
        CurrentRoomCode = room.RoomCode;
        RoomCodeInput = room.RoomCode;
        GameMode = "online";
        HasActiveMatch = false;
        var member = room.Members.FirstOrDefault(m => m.SessionId == sessionId);
        sessionToken = member?.SessionToken ?? string.Empty;
        MultiplayerStatusMessage = CreatePublicRoom ? $"Room {room.RoomCode} created. Visible in public lobby." : $"Room {room.RoomCode} created. Share the invitation link.";
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

        var (room, result) = multiplayerGameService.JoinRoom(RoomCodeInput, sessionId, BrowserPlayerId, MultiplayerName, sessionToken);
        switch (result)
        {
            case MultiplayerGameService.JoinRoomResult.RoomNotFound:
                MultiplayerStatusMessage = "Room not found. Check the code and try again.";
                NotifyStateChanged();
                return false;
            case MultiplayerGameService.JoinRoomResult.RoomFull:
                MultiplayerStatusMessage = "This room is full. Try a different room.";
                NotifyStateChanged();
                return false;
            case MultiplayerGameService.JoinRoomResult.GameStarted:
                MultiplayerStatusMessage = "This game has already started. Create or join a new room.";
                NotifyStateChanged();
                return false;
            case MultiplayerGameService.JoinRoomResult.NameTaken:
                MultiplayerStatusMessage = "That name is already taken in this room. Choose another name.";
                NotifyStateChanged();
                return false;
            case MultiplayerGameService.JoinRoomResult.SessionTokenMismatch:
                MultiplayerStatusMessage = "Unable to verify your session. This seat belongs to another player.";
                NotifyStateChanged();
                return false;
        }

        if (room is null)
        {
            MultiplayerStatusMessage = "Unable to join room. Please try again.";
            NotifyStateChanged();
            return false;
        }

        CurrentRoom = room;
        CurrentRoomCode = room.RoomCode;
        BoardTheme = room.BoardTheme;
        CardTheme = room.CardTheme;
        GameMode = "online";
        var member = room.Members.FirstOrDefault(m => m.SessionId == sessionId);
        sessionToken = member?.SessionToken ?? string.Empty;
        MultiplayerStatusMessage = $"Joined room {room.RoomCode}.";
        RefreshAvailableRooms();
        NotifyStateChanged();
        return await Task.FromResult(true);
    }

    public async Task<bool> TryRestoreOnlineRoomAsync(string? roomCode, string? playerName, string? savedToken = null)
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
        var (room, result) = multiplayerGameService.JoinRoom(RoomCodeInput, sessionId, BrowserPlayerId, MultiplayerName, savedToken);
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
        var member = room.Members.FirstOrDefault(m => m.SessionId == sessionId);
        sessionToken = member?.SessionToken ?? string.Empty;
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

        var started = await multiplayerGameService.StartGameAsync(CurrentRoomCode, sessionId, sessionToken);
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
            await multiplayerGameService.AdvanceRoundAsync(CurrentRoomCode, sessionId, sessionToken);
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
            await multiplayerGameService.LeaveRoomAsync(CurrentRoomCode, sessionId, sessionToken);
        }

        CurrentRoom = null;
        CurrentRoomCode = null;
        HasActiveMatch = false;
        MultiplayerStatusMessage = string.Empty;
        lock (recentEmojis) recentEmojis.Clear();
        RefreshAvailableRooms();
        NotifyStateChanged();
    }

    public void ReturnToMenu()
    {
        CancelTurnTimer();
        if (GameMode == "online")
        {
            _ = LeaveOnlineRoomSafeAsync();
        }

        HasActiveMatch = false;
        IsBusy = false;
        IsDealing = false;
        RefreshAvailableRooms();
        NotifyStateChanged();
    }

    private async Task LeaveOnlineRoomSafeAsync()
    {
        try
        {
            await LeaveOnlineRoomAsync();
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Leave room error: {ex.Message}");
        }
    }

    public void SetBrowserPlayerId(string? browserPlayerId)
    {
        BrowserPlayerId = string.IsNullOrWhiteSpace(browserPlayerId) ? sessionId : browserPlayerId.Trim();
        RefreshAvailableRooms();
    }

    public Task<bool> SendEmojiAsync(string emoji)
    {
        if (GameMode != "online" || CurrentRoomCode is null || !IsOnlineGameStarted)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(multiplayerGameService.SendEmoji(CurrentRoomCode, sessionId, emoji, sessionToken));
    }

    private void HandleEmojiReceived(string roomCode, EmojiMessage message)
    {
        CancellationToken token;
        lock (recentEmojis)
        {
            if (disposed || ReactionsMuted || CurrentRoomCode is null ||
                !string.Equals(CurrentRoomCode, roomCode, StringComparison.OrdinalIgnoreCase)) return;
            token = clockUpdates.Token;
            recentEmojis.RemoveAll(item => item.PlayerIndex == message.PlayerIndex);
            recentEmojis.Add(message);
        }
        NotifyStateChanged();
        _ = ClearEmojiAfterDelayAsync(message, token);
    }

    private async Task ClearEmojiAfterDelayAsync(EmojiMessage message, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool removed;
        lock (recentEmojis) removed = !disposed && recentEmojis.Remove(message);
        if (removed) NotifyStateChanged();
    }

    public async Task PlaceBidAsync(int bid)
    {
        CancelTurnTimer();
        if (IsBusy)
        {
            return;
        }

        if (GameMode == "online" && CurrentRoomCode is not null)
        {
            var revision = Engine?.Revision ?? 0;
            await multiplayerGameService.PlaceBidAsync(CurrentRoomCode, sessionId, bid, revision, sessionToken);
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
        CancelTurnTimer();
        if (IsBusy)
        {
            return;
        }

        if (GameMode == "online" && CurrentRoomCode is not null)
        {
            var revision = Engine?.Revision ?? 0;
            await multiplayerGameService.PlayCardAsync(CurrentRoomCode, sessionId, card, revision, sessionToken);
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
        var matchScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in Engine.Players)
        {
            matchScores[player.Name] = player.Score;
        }
        matchHistory.Add(new MatchHistoryRecord(
            GameMode,
            Engine.RoundNumber,
            Engine.DealerIndex,
            Engine.LeaderIndex,
            matchScores,
            DateTimeOffset.UtcNow
        ));
        if (matchHistory.Count > MaxMatchHistory)
        {
            matchHistory.RemoveRange(0, matchHistory.Count - MaxMatchHistory);
        }
        PersistMatchHistory();
        NotifyStateChanged();
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

    public IReadOnlyList<MatchHistoryRecord> GetMatchHistory() => matchHistory.AsReadOnly();

    public void LoadMatchHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var records = System.Text.Json.JsonSerializer.Deserialize<List<MatchHistoryRecordDto>>(json);
            if (records is null) return;
            matchHistory = records.Select(r => new MatchHistoryRecord(
                r.GameMode ?? "practice",
                r.RoundNumber,
                r.DealerIndex,
                r.LeaderIndex,
                r.Scores ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                r.CompletedAt
            )).ToList();
            if (matchHistory.Count > MaxMatchHistory)
            {
                matchHistory.RemoveRange(0, matchHistory.Count - MaxMatchHistory);
            }
        }
        catch { }
    }

    public string SaveMatchHistory()
    {
        return System.Text.Json.JsonSerializer.Serialize(matchHistory.Select(r => new MatchHistoryRecordDto
        {
            GameMode = r.GameMode,
            RoundNumber = r.RoundNumber,
            DealerIndex = r.DealerIndex,
            LeaderIndex = r.LeaderIndex,
            Scores = r.Scores,
            CompletedAt = r.CompletedAt
        }));
    }

    public async Task LoadMatchHistoryFromStorageAsync()
    {
        var json = await storage.GetItemAsync(MatchHistoryStorageKey);
        LoadMatchHistory(json);
    }

    private void PersistMatchHistory()
    {
        var json = SaveMatchHistory();
        _ = storage.SetItemAsync(MatchHistoryStorageKey, json);
    }

    private sealed class MatchHistoryRecordDto
    {
        public string? GameMode { get; set; }
        public int RoundNumber { get; set; }
        public int DealerIndex { get; set; }
        public int LeaderIndex { get; set; }
        public Dictionary<string, int>? Scores { get; set; }
        public DateTimeOffset CompletedAt { get; set; }
    }

    private void StartTurnTimer()
    {
        CancelTurnTimer();
        turnTimerFired = false;
        TurnTimeRemainingMs = TurnTimeoutMs;

        turnTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                turnTimerFired = true;
                CancelTurnTimer();
                await AutoPlayOnTimeout();
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Turn timer error: {ex.Message}");
            }
        }, null, TurnTimeoutMs, Timeout.Infinite);

        _ = RunTimerTickAsync();
        NotifyStateChanged();
    }

    private async Task RunTimerTickAsync()
    {
        var token = lifecycleCts.Token;
        try
        {
            while (TurnTimeRemainingMs > 0 && !turnTimerFired && !disposed && !token.IsCancellationRequested)
            {
                await Task.Delay(1000, token);
                TurnTimeRemainingMs -= 1000;
                NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private void CancelTurnTimer()
    {
        turnTimer?.Dispose();
        turnTimer = null;
        TurnTimeRemainingMs = 0;
        NotifyStateChanged();
    }

    private async Task AutoPlayOnTimeout()
    {
        if (IsBusy)
        {
            return;
        }

        if (localEngine.Phase == GamePhase.Bidding && localEngine.CurrentPlayer.IsHuman)
        {
            var allowed = localEngine.GetAllowedBids(localEngine.CurrentTurnIndex);
            var bid = allowed.Count > 0 ? allowed[0] : 0;
            await PlaceBidAsync(bid);
        }
        else if (localEngine.Phase == GamePhase.TrickPlaying && localEngine.CurrentPlayer.IsHuman)
        {
            var legal = localEngine.GetLegalCards(localEngine.CurrentTurnIndex);
            if (legal.Count > 0)
            {
                var card = legal.OrderBy(c => c.Rank).First();
                await PlayCardAsync(card);
            }
        }
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

        if (localEngine.CurrentPlayer.IsHuman && localEngine.Phase is GamePhase.Bidding or GamePhase.TrickPlaying && !localEngine.IsTrickResolutionPending)
        {
            StartTurnTimer();
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
        var token = lifecycleCts.Token;
        try
        {
            IsBusy = true;
            IsDealing = true;
            DealStep = 0;
            NotifyStateChanged();

            var totalDeals = Engine.Players.Count * Engine.CardsPerPlayer;
            while (DealStep < totalDeals && !disposed && !token.IsCancellationRequested)
            {
                await Task.Delay(DealStepDelayMs, token);
                DealStep++;
                NotifyStateChanged();
            }

            await Task.Delay(120, token);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            if (!disposed)
            {
                IsDealing = false;
                IsBusy = false;
                NotifyStateChanged();
            }
        }
    }

    private async void HandleRoomUpdated(string roomCode)
    {
        try
        {
            if (disposed) return;
            RefreshAvailableRooms();

            if (CurrentRoomCode != roomCode)
            {
                NotifyStateChanged();
                return;
            }

            await RefreshRoomAsync();
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HandleRoomUpdated error: {ex.Message}");
        }
    }

    private async Task RefreshRoomAsync()
    {
        if (CurrentRoomCode is null)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref refreshSemaphore, 1, 0) != 0)
        {
            return;
        }

        try
        {
            CurrentRoom = multiplayerGameService.GetRoom(CurrentRoomCode);
            if (CurrentRoom is not null)
            {
                BoardTheme = CurrentRoom.BoardTheme;
                CardTheme = CurrentRoom.CardTheme;
            }
            else
            {
                CurrentRoomCode = null;
                lock (recentEmojis) recentEmojis.Clear();
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
        }
        finally
        {
            Interlocked.Exchange(ref refreshSemaphore, 0);
        }

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
        if (string.IsNullOrWhiteSpace(MultiplayerName))
        {
            var shortId = BrowserPlayerId.Length >= 6 ? BrowserPlayerId[..6] : BrowserPlayerId;
            MultiplayerName = $"Guest {shortId}";
        }
    }

    public void Dispose()
    {
        lock (recentEmojis)
        {
            if (disposed) return;
            disposed = true;
            recentEmojis.Clear();
        }
        lifecycleCts.Cancel();
        lifecycleCts.Dispose();
        clockUpdates.Cancel();
        clockUpdates.Dispose();
        CancelTurnTimer();
        multiplayerGameService.RoomUpdated -= HandleRoomUpdated;
        multiplayerGameService.EmojiReceived -= HandleEmojiReceived;
    }
}
