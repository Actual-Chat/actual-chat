using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<TranslationSourceId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<TranslationSourceId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<TranslationSourceId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<TranslationSourceId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]

public partial class TranslationSourceId  : StringIdentifier, IStringIdentifier<TranslationSourceId>
{
    private static readonly ILruCache<string, TranslationSourceId> Cache = CreateCache<TranslationSourceId>(256);

    public const char Delimiter = ':';

    public static TranslationSourceId New(TextEntryId textEntryId)
        => new TranslationSourceId(textEntryId.Value, textEntryId);

    public static TranslationSourceId New(ChatId chatId, TranslationIdKind kind, long refLid)
        => new TranslationSourceId(Format(chatId, kind, refLid.ToInvariantString()), chatId, kind, refLid);

    private TranslationSourceId(string value, TextEntryId textEntryId)
        : this(value, textEntryId.ChatId, TranslationIdKind.TextEntry, textEntryId.LocalId)
        => ChatEntryId = textEntryId;

    private TranslationSourceId(string value, ChatId chatId, TranslationIdKind kind, long refLid)
        : this(value, chatId, kind, refLid.ToInvariantString())
        => RefLid = refLid;

    private TranslationSourceId(string value, ChatId chatId, TranslationIdKind kind, string extra)
        :base(value)
    {
        ChatId = chatId;
        Kind = kind;
        Extra = extra;
    }

    public ChatId ChatId { get; }
    public TranslationIdKind Kind { get; }
    public string Extra { get; }
    private long RefLid { get; }
    private TextEntryId? ChatEntryId { get; }

    public TextEntryId GetChatEntryId()
    {
        if (Kind != TranslationIdKind.TextEntry)
            throw StandardError.Constraint("Supported only for TextEntry TranslationId");

        return ChatEntryId!;
    }

    public long GetRefLId()
        => RefLid;

    public TranslationId ToTranslationId(Language language)
        => TranslationId.New(this, language);

    // Equality

    public bool Equals(TranslationSourceId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is TranslationSourceId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TranslationSourceId? left, TranslationSourceId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TranslationSourceId? left, TranslationSourceId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId chatId, TranslationIdKind kind, string extra)
        => $"{chatId.Value}{Delimiter}{((int)kind).ToInvariantString()}{Delimiter}{extra}";

    public static TranslationSourceId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<TranslationSourceId>(s);

    public static TranslationSourceId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static TranslationSourceId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out TranslationSourceId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var chatIdLength = s.IndexOf(Delimiter);
        var s1 = s.Substring(0, chatIdLength);
        if (!ChatId.TryParse(s1, out var chatId))
            return false;

        var kindStartIndex = chatIdLength + 1;
        var kindEndIndex = s.IndexOf(Delimiter, kindStartIndex);
        var s2 = s.Substring(kindStartIndex, kindEndIndex - kindStartIndex);
        if (!int.TryParse(s2, CultureInfo.InvariantCulture, out var iKind))
            return false;

        var kind = (TranslationIdKind)iKind;
        if (!Enum.IsDefined(kind))
            return false;

        var extraStartIndex = kindEndIndex + 1;
        var s3 = s.Substring(extraStartIndex);
        if (!long.TryParse(s3, CultureInfo.InvariantCulture, out var lid) || lid <= 0)
            return false;

        if (kind is TranslationIdKind.TextEntry)
            result = new TranslationSourceId(s, TextEntryId.New(chatId, lid));
        else
            result = new TranslationSourceId(s, chatId, kind, lid);

        result = Cache.AddOrGet(s, result);
        return true;
    }
}

public enum TranslationIdKind
{
    TextEntry = 0,
    ConversationTitle = 1,
    ConversationDescription = 2,
    ConversationSummary = 3,
    ThreadTitle = 4,
    ThreadDescription = 5,
}
