namespace ActualChat;

/// <summary>
/// One of the supported mention target categories. Holds the textual prefix used in
/// the serialized <see cref="MentionRef"/> value (e.g. <c>"u"</c> in <c>u:userId</c>)
/// and the parser that turns the rest of the value into an <see cref="IMentionTarget"/>.
/// </summary>
public sealed class MentionKind
{
    public delegate bool TryParseHandler<T>(string? s, [NotNullWhen(true)] out T? value);
    public delegate bool TryParseTargetIdHandler(string? s, [NotNullWhen(true)] out IMentionTarget? target);

    public static readonly Dictionary<string, MentionKind> ByPrefix = new(StringComparer.Ordinal);

    public static readonly MentionKind Author = Register<AuthorId>("a", nameof(Author), AuthorId.TryParse);
    public static readonly MentionKind User = Register<UserId>("u", nameof(User), UserId.TryParse);
    public static readonly MentionKind Chat = Register<ChatId>("c", nameof(Chat), ChatId.TryParse);
    public static readonly MentionKind Place = Register<PlaceId>("p", nameof(Place), PlaceId.TryParse);
    public static readonly MentionKind Emoji = Register<EmojiRef>("e", nameof(Emoji), EmojiRef.TryParse);

    private readonly TryParseTargetIdHandler _tryParseTargetIdHandler;

    public string Name { get; }
    public string FullName { get; }
    public string Prefix { get; }

    private MentionKind(string prefix, string name, TryParseTargetIdHandler tryParseTargetIdHandler)
    {
        Name = name;
        FullName = $"{nameof(MentionKind)}.{Name}";
        Prefix = prefix;
        _tryParseTargetIdHandler = tryParseTargetIdHandler;
    }

    public bool TryParseTarget(string? s, [NotNullWhen(true)] out IMentionTarget? target)
        => _tryParseTargetIdHandler.Invoke(s, out target);

    public override string ToString()
        => FullName;

    // Private methods

    private static MentionKind Register<T>(string prefix, string name, TryParseHandler<T> tryParse)
        where T : class, IMentionTarget
    {
        var kind = new MentionKind(prefix, name, TryParseTarget);
        ByPrefix.Add(prefix, kind);
        return kind;

        bool TryParseTarget(string? s, [NotNullWhen(true)] out IMentionTarget? target) {
            if (tryParse(s, out var typed)) {
                target = typed;
                return true;
            }
            target = null;
            return false;
        }
    }
}
