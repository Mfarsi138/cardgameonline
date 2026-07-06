using System.Threading;
using OhHell.Core;

namespace OhHell.Components.Models;

public sealed class OnlineGameRoom
{
    public string RoomCode { get; init; } = string.Empty;
    public string HostSessionId { get; set; } = string.Empty;
    public int MaxPlayers { get; set; } = 4;
    public string BoardTheme { get; set; } = "emerald";
    public string CardTheme { get; set; } = "svg";
    public bool Started { get; set; }
    public List<OnlineRoomMember> Members { get; } = new();
    public GameEngine? Engine { get; set; }
    public SemaphoreSlim SyncLock { get; } = new(1, 1);
}
