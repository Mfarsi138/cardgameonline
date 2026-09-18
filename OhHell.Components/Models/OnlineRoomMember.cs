namespace OhHell.Components.Models;

public sealed class OnlineRoomMember
{
    internal List<DateTimeOffset> EmojiTimestamps { get; } = [];
    public string SessionId { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public bool IsBot { get; set; }
    public int? PlayerIndex { get; set; }
}
