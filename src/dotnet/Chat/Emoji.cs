namespace ActualChat.Chat;

public record Emoji(string Code, string Name)
{
    private static readonly Dictionary<string, Emoji> _all = new Emoji[] {
        new ("👍", "thumbs up"),
        new ("👌", "ok hand"),
        new ("🔥", "fire"),
        new ("❤️", "red heart"),
        new ("😁", "beaming face with smiling eyes"),
        new ("👎", "thumbs down"),
        new ("😢", "crying face"),
        new ("😱", "face screaming in fear"),
        new ("🎃", "jack-o-lantern"),
    }.ToDictionary(x => x.Code, StringComparer.Ordinal);

    public static bool IsAllowed(string emoji)
        => _all.ContainsKey(emoji);

    public static readonly IReadOnlyCollection<Emoji> All = _all.Values;
}
