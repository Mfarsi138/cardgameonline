namespace OhHell.Core;

public enum BotDifficulty { Easy, Medium, Hard }

public sealed record PlayerDefinition(string Name, bool IsHuman, BotDifficulty Difficulty = BotDifficulty.Hard);
