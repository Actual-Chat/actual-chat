namespace ActualChat;

/// <summary>
/// One of the supported mention target categories. Holds the textual prefix used in
/// the serialized <see cref="MentionId"/> value (e.g. <c>"u"</c> in <c>u:userId</c>)
/// and the parser that turns the rest of the value into an <see cref="IMentionTarget"/>.
/// </summary>
public sealed class MentionKind
{
    public delegate bool TryParseDelegate<T>(string? s, [NotNullWhen(true)] out T? value);
    public delegate bool TargetParser(string? s, [NotNullWhen(true)] out IMentionTarget? target);

    private static readonly Dictionary<string, MentionKind> _byPrefix = new(StringComparer.Ordinal);

    public static readonly MentionKind Author = Register<AuthorId>("a", nameof(Author), AuthorId.TryParse);
    public static readonly MentionKind User = Register<UserId>("u", nameof(User), UserId.TryParse);
    public static readonly MentionKind Chat = Register<ChatId>("c", nameof(Chat), ChatId.TryParse);
    public static readonly MentionKind Place = Register<PlaceId>("p", nameof(Place), PlaceId.TryParse);
    public static readonly MentionKind Emoji = Register<EmojiRef>("e", nameof(Emoji), EmojiRef.TryParse);
    public static readonly MentionKind Gif = Register<GifRef>("g", nameof(Gif), GifRef.TryParse);

    public static IReadOnlyDictionary<string, MentionKind> ByPrefix => _byPrefix;

    private readonly TargetParser _tryParse;

    public string Prefix { get; }
    public string Name { get; }

    private MentionKind(string prefix, string name, TargetParser tryParse)
    {
        Prefix = prefix;
        Name = name;
        _tryParse = tryParse;
    }

    public bool TryParseTarget(string? s, [NotNullWhen(true)] out IMentionTarget? target)
        => _tryParse(s, out target);

    public override string ToString()
        => Name;

    // Private methods

    private static MentionKind Register<T>(string prefix, string name, TryParseDelegate<T> tryParse)
        where T : class, IMentionTarget
    {
        var kind = new MentionKind(prefix, name, Adapt);
        _byPrefix.Add(prefix, kind);
        return kind;

        bool Adapt(string? s, [NotNullWhen(true)] out IMentionTarget? target) {
            if (tryParse(s, out var typed)) {
                target = typed;
                return true;
            }
            target = null;
            return false;
        }
    }
}
