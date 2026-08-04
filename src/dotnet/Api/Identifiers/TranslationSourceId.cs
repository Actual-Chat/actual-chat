using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Identifies the source content for a translation.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<TranslationSourceId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<TranslationSourceId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<TranslationSourceId>))]
[TypeConverter(typeof(StringLikeTypeConverter<TranslationSourceId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]

public partial class TranslationSourceId  : StringIdentifier, IStringIdentifier<TranslationSourceId>
{
    private static readonly ILruCache<string, TranslationSourceId> Cache = CreateCache<TranslationSourceId>(256);

    public const char Delimiter = ':';

    public static TranslationSourceId New(ChatId chatId, TranslationIdKind kind, long refLid)
        => new (Format(chatId, kind, refLid.ToString()), chatId, kind, refLid);

    public static TranslationSourceId New(ChatEntryId chatEntryId)
        => new (chatEntryId.Value, chatEntryId);

    public static TranslationSourceId New(ThreadChatId threadChatId, ThreadTranslationIdKind kind)
        => New(threadChatId.ParentChatId, Map(kind), threadChatId.ThreadId);

    public static TranslationSourceId New(ConversationId conversationId, ConversationTranslationIdKind kind)
        => New(conversationId.ChatId, Map(kind), conversationId.StartEntryLid);

    private TranslationSourceId(string value, ChatEntryId chatEntryId)
        : this(value, chatEntryId.ChatId, TranslationIdKind.ChatEntry, chatEntryId.LocalId)
        => EntryId = chatEntryId;

    private TranslationSourceId(string value, ChatId chatId, TranslationIdKind kind, long refLid)
        : this(value, chatId, kind, refLid.ToString())
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
    public long RefLid { get; }

    private ChatEntryId? EntryId { get; }

    public ChatEntryId GetChatEntryId()
    {
        if (Kind != TranslationIdKind.ChatEntry)
            throw StandardError.Constraint("Supported only for ChatEntry TranslationId");

        return EntryId!;
    }

    public TranslationId ToTranslationId(Language language)
        => TranslationId.New(this, language);

    // Equality

    public bool Equals(TranslationSourceId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);

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
        => $"{chatId.Value}{Delimiter}{((int)kind).ToString()}{Delimiter}{extra}";

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
        if (!int.TryParse(s2, out var iKind))
            return false;

        var kind = (TranslationIdKind)iKind;
        if (!Enum.IsDefined(kind))
            return false;

        var extraStartIndex = kindEndIndex + 1;
        var s3 = s.Substring(extraStartIndex);
        if (!long.TryParse(s3, out var lid) || lid <= 0)
            return false;

        if (kind is TranslationIdKind.ChatEntry)
            result = new TranslationSourceId(s, ChatEntryId.New(chatId, lid));
        else
            result = new TranslationSourceId(s, chatId, kind, lid);

        result = Cache.AddOrGet(s, result);
        return true;
    }

    private static TranslationIdKind Map(ConversationTranslationIdKind kind)
        => kind switch {
            ConversationTranslationIdKind.Title => TranslationIdKind.ConversationTitle,
            ConversationTranslationIdKind.Description => TranslationIdKind.ConversationDescription,
            ConversationTranslationIdKind.Summary => TranslationIdKind.ConversationSummary,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static TranslationIdKind Map(ThreadTranslationIdKind kind)
        => kind switch {
            ThreadTranslationIdKind.Title => TranslationIdKind.ThreadTitle,
            ThreadTranslationIdKind.Description => TranslationIdKind.ThreadDescription,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}

/// <summary>
/// Specifies the type of content being translated.
/// </summary>
public enum TranslationIdKind
{
    ChatEntry = 0,
    ConversationTitle = 5,
    ConversationDescription = 6,
    ConversationSummary = 7,
    ThreadTitle = 8,
    ThreadDescription = 9,
}

/// <summary>
/// Specifies conversation-specific content types for translation.
/// </summary>
public enum ConversationTranslationIdKind
{
    Title,
    Description,
    Summary,
}

/// <summary>
/// Specifies thread-specific content types for translation.
/// </summary>
public enum ThreadTranslationIdKind
{
    Title,
    Description,
}
