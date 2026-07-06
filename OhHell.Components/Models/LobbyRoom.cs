namespace OhHell.Components.Models;

public sealed class LobbyRoom
{
    public string RoomCode { get; init; } = string.Empty;
    public string HostConnectionId { get; init; } = string.Empty;
    public List<string> Players { get; } = new();
}
