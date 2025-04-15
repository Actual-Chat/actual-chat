using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<ConversationId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ConversationId2>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<ConversationId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ConversationId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class ConversationId2 : StringIdentifier, IStringIdentifier<ConversationId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ConversationId2>();
    private static readonly ILruCache<string, ConversationId2> Cache = CreateCache<ConversationId2>(128);

    public const char Delimiter = ':';

    [IgnoreDataMember]
    public ChatId2 ChatId { get; }
    [IgnoreDataMember]
    public long StartEntryLid { get; }

    // Factories and constructors

    public static ConversationId2 New(ChatId2 chatId, long startEntryLid)
    {
        if (startEntryLid < 0)
            throw new ArgumentOutOfRangeException(nameof(startEntryLid));

        return new(Format(chatId, startEntryLid), chatId, startEntryLid);
    }

    private ConversationId2(string value, ChatId2 chatId, long startEntryLid) : base(value)
    {
        ChatId = chatId;
        StartEntryLid = startEntryLid;
    }

    // Equality

    public bool Equals(ConversationId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is ConversationId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ConversationId2? left, ConversationId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ConversationId2? left, ConversationId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId2 chatId, long startEntryLid)
        => $"{chatId.Value}{Delimiter}{startEntryLid.Format()}";

    public static ConversationId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ConversationId2>(s);

    public static ConversationId2? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ConversationId2? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var chatIdLength = s.OrdinalIndexOf(Delimiter);
        if (chatIdLength < 0)
            return false;

        if (!ChatId2.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var sStartEntryLid = s.AsSpan(chatIdLength + 1);
        if (!NumberExt.TryParsePositiveLong(sStartEntryLid, out var startEntryLid))
            return false;

        result = new ConversationId2(s, chatId, startEntryLid);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
