namespace ActualChat.Chat;

public record Emoji(string Code, string Name)
{
    public static readonly Emoji Unknown = new("", "");
    public static readonly Emoji ThumbsUp = new("👍", "thumbs up");
    public static readonly Emoji RedHeart = new ("❤️", "red heart");
    public static readonly Emoji Lol = new ("😂", "face with tears of joy");
    public static readonly Emoji Surprise = new ("😲", "astonished face");
    public static readonly Emoji Sad = new ("😥", "sad but relieved face");
    public static readonly Emoji Angry = new ("😠", "angry face");
    public static readonly Emoji Poo = new ("💩", "pile of poo");
    public static readonly Emoji OkHand = new ("👌", "ok hand");
    public static readonly Emoji Fire = new ("🔥", "fire");
    public static readonly Emoji BeamingFace = new ("😁", "beaming face with smiling eyes");
    public static readonly Emoji ThumbsDown = new ("👎", "thumbs down");
    public static readonly Emoji ScreamingFaceInFear = new ("😱", "face screaming in fear");
    public static readonly Emoji JackOLantern = new ("🎃", "jack-o-lantern");
    private static readonly Dictionary<string, Emoji> _all = new[] {
        ThumbsUp,
        RedHeart,
        Lol,
        Surprise,
        Sad,
        Angry,
        Poo,
        OkHand,
        Fire,
        BeamingFace,
        ThumbsDown,
        ScreamingFaceInFear,
        JackOLantern,
    }.ToDictionary(x => x.Code, StringComparer.Ordinal);
    public static readonly IReadOnlyCollection<Emoji> All = _all.Values;

    public static bool IsAllowed(string emoji)
        => _all.ContainsKey(emoji);

    public static implicit operator string(Emoji emoji) => emoji.Code;
    public static implicit operator Emoji(string code) => _all.GetValueOrDefault(code, Unknown);

    public virtual bool Equals(Emoji? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return OrdinalEquals(Code, other.Code);
    }

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Code);

    public override string ToString()
        => Code;
}
