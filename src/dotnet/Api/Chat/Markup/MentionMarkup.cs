using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Base markup element for a mention. Typed subclasses (e.g. <see cref="AuthorMention"/>,
/// <see cref="UserMention"/>) carry pre-resolved data so render-time access is synchronous.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial class MentionMarkup(MentionRef id, string name = "") : Markup
{
    public static readonly string NotAvailableName = "(n/a)";
    public static readonly Func<MentionMarkup, string> DefaultFormatter = m => m.Format();
    public static readonly Func<MentionMarkup, string> NameOrNotAvailableFormatter = m => "@" + m.NameOrNotAvailable;
    public static readonly Func<MentionMarkup, string> NameOrIdFormatter = m => "@" + m.NameOrId;
    public static readonly Func<MentionMarkup, string> ReadableFormatter = FormatReadable;

    public const char ReadableSpace = '\u2005'; // four-per-em space — joins multi-word names without confusing word boundaries

    private static string FormatReadable(MentionMarkup m) => m switch {
        EmojiMention em => FormatEmojiReadable(em),
        ChatMention or PlaceMention => "@\"" + m.NameOrNotAvailable + "\"",
        _ => "@" + JoinWithReadableSpace(m.NameOrNotAvailable),
    };

    private static string FormatEmojiReadable(EmojiMention em)
    {
        var text = em.EmojiRef.Text;
        if (Emojis.BySymbol.ContainsKey(text))
            return text; // Standard unicode emoji — copy the glyph itself.
        return ":" + text + ":"; // Custom emoji — copy its id, e.g. :ginger-clown:.
    }

    private static string JoinWithReadableSpace(string name)
    {
        if (name.IndexOf(' ') < 0)
            return name;
        return name.Replace(' ', ReadableSpace);
    }

    [DataMember, MemoryPackOrder(0), Key(0)]
    public MentionRef Id { get; } = id;
    [DataMember, MemoryPackOrder(1), Key(1)]
    public string Name { get; } = name;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string QuotedName => Quote(Name);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string NameOrNotAvailable => Name.NullIfEmpty() ?? NotAvailableName;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string NameOrId => Name.NullIfEmpty() ?? Id.Value;

    public static MentionMarkup New(MentionRef id, string name = "")
    {
        var kind = id.Kind;
        if (kind == MentionKind.Author) return new AuthorMention(id, name);
        if (kind == MentionKind.User) return new UserMention(id, name);
        if (kind == MentionKind.Chat) return new ChatMention(id, name);
        if (kind == MentionKind.Place) return new PlaceMention(id, name);
        if (kind == MentionKind.Emoji) return new EmojiMention(id, name);
        return new MentionMarkup(id, name);
    }

    public override string Format()
        => Name.IsNullOrEmpty()
            ? "@" + Id
            : string.Concat("@", QuotedName, Id);

    public static string Quote(string name)
        => string.Concat("`", name.Replace("`", "``"), "`");
}
