using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Identifies a conversation within a chat, starting from a specific entry.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<ConversationId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<ConversationId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<ConversationId>))]
[TypeConverter(typeof(StringLikeTypeConverter<ConversationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class ConversationId : StringIdentifier, IStringIdentifier<ConversationId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ConversationId>();
    private static readonly ILruCache<string, ConversationId> Cache = CreateCache<ConversationId>(128);

    public const char Delimiter = ':';

    [IgnoreDataMember]
    public ChatId ChatId { get; }
    [IgnoreDataMember]
    public long StartEntryLid { get; }

    // Factories and constructors

    public static ConversationId New(ChatId chatId, long startEntryLid)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startEntryLid);

        return new(Format(chatId, startEntryLid), chatId, startEntryLid);
    }

    private ConversationId(string value, ChatId chatId, long startEntryLid) : base(value)
    {
        ChatId = chatId;
        StartEntryLid = startEntryLid;
    }

    // Equality

    public bool Equals(ConversationId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is ConversationId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ConversationId? left, ConversationId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ConversationId? left, ConversationId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId chatId, long startEntryLid)
        => $"{chatId.Value}{Delimiter}{startEntryLid.Format()}";

    public static ConversationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ConversationId>(s);

    public static ConversationId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ConversationId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ConversationId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var chatIdLength = s.IndexOf(Delimiter);
        if (chatIdLength < 0)
            return false;

        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var sStartEntryLid = s.AsSpan(chatIdLength + 1);
        if (!NumberExt.TryParsePositiveLong(sStartEntryLid, out var startEntryLid))
            return false;

        result = new ConversationId(s, chatId, startEntryLid);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
