using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Base markup element for a mention. Typed subclasses (e.g. <see cref="AuthorMention"/>,
/// <see cref="UserMention"/>) carry pre-resolved data so render-time access is synchronous.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract]
public abstract partial class MentionMarkup(MentionRef id, string name = "") : Markup
{
    // Four-per-em space — joins multi-word names without confusing word boundaries
    public const char ReadableSpace = '\u2005';
    public static readonly string NotAvailableName = "(n/a)";
    public static readonly Func<MentionMarkup, string> DefaultFormatter = m => m.Format();
    public static readonly Func<MentionMarkup, string> NameOrNotAvailableFormatter = m => "@" + m.NameOrNotAvailable;
    public static readonly Func<MentionMarkup, string> NameOrIdFormatter = m => "@" + m.NameOrId;
    public static readonly Func<MentionMarkup, string> ReadableFormatter = FormatReadable;
    [DataMember, Key(0)]
    public MentionRef Id { get; } = id;
    [DataMember, Key(1)]
    public string Name { get; } = name;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string QuotedName => Quote(Name);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string NameOrNotAvailable => Name.NullIfEmpty() ?? NotAvailableName;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string NameOrId => Name.NullIfEmpty() ?? Id.Value;

    public static MentionMarkup New(MentionRef id, string name = "")
    {
        var kind = id.Kind;
        if (kind == MentionKind.Author)
            return new AuthorMention(id, name);
        if (kind == MentionKind.User)
            return new UserMention(id, name);
        if (kind == MentionKind.Chat)
            return new ChatMention(id, name);
        if (kind == MentionKind.Place)
            return new PlaceMention(id, name);
        if (kind == MentionKind.Emoji)
            return new EmojiMention(id, name);

        throw new ArgumentOutOfRangeException(nameof(id), $"Unsupported mention kind: {kind}");
    }

    public static string Quote(string name)
        => string.Concat("`", name.Replace("`", "``"), "`");

    public override string Format()
        => Name.IsNullOrEmpty()
            ? "@" + Id
            : string.Concat("@", QuotedName, Id);

    // Private methods

    private static string FormatReadable(MentionMarkup m) => m switch {
        EmojiMention em => FormatEmojiReadable(em),
        ChatMention or PlaceMention => "@\"" + m.NameOrNotAvailable + "\"",
        _ => "@" + JoinWithReadableSpace(m.NameOrNotAvailable),
    };

    private static string FormatEmojiReadable(EmojiMention em)
    {
        // A standard unicode emoji copies as its glyph; a custom one copies as :its-id:.
        var text = em.EmojiRef.Text;
        if (Emojis.BySymbol.ContainsKey(text))
            return text;

        return ":" + text + ":";
    }

    private static string JoinWithReadableSpace(string name)
        => name.Replace(' ', ReadableSpace);
}
