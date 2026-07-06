namespace OhHell.Components.Models;

public sealed class OnlineRoomSummary
{
    public string RoomCode { get; init; } = string.Empty;
    public string HostName { get; init; } = string.Empty;
    public int PlayerCount { get; init; }
    public int MaxPlayers { get; init; }
    public bool Started { get; init; }
    public string BoardTheme { get; init; } = "emerald";
    public string CardTheme { get; init; } = "svg";
    public bool CanJoin { get; init; }
    public bool CanRejoin { get; init; }
}
