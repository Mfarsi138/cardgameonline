namespace OhHell.Components.Models;

public sealed record EmojiMessage(Guid Id, int PlayerIndex, string PlayerName, string Emoji, DateTimeOffset SentAt)
{
    public static IReadOnlyList<string> PresetEmojis { get; } = Array.AsReadOnly(new[] { "👍", "👎", "😊", "😂", "🔥", "💀", "🎉", "😱", "🤔", "❤️", "🫡", "🏆" });

    public static bool IsValidEmoji(string emoji) => PresetEmojis.Contains(emoji);
}
