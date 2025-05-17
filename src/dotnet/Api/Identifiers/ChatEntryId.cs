using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<ChatEntryId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ChatEntryId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<ChatEntryId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ChatEntryId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
// This type is technically abstract, but we don't want to make it abstract,
// coz this forces RPC to use polymorphic serialization for this type.
public partial class ChatEntryId : StringIdentifier, IStringIdentifier<ChatEntryId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatEntryId>();
    private static readonly ILruCache<string, ChatEntryId> Cache = CreateCache<ChatEntryId>(2048);

    [IgnoreDataMember]
    public ChatId ChatId { get; }
    [IgnoreDataMember]
    public ChatEntryKind Kind { get; }
    [IgnoreDataMember]
    public long LocalId { get; }

    // Factories and constructors

    public static ChatEntryId New(ChatId chatId, ChatEntryKind kind, long localId)
        => kind switch {
            ChatEntryKind.Text => TextEntryId.New(chatId, localId),
            ChatEntryKind.Audio => AudioEntryId.New(chatId, localId),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    protected ChatEntryId(string value, ChatId chatId, ChatEntryKind kind, long localId) : base(value)
    {
        ChatId = chatId;
        Kind = kind;
        LocalId = localId;
    }

    // Equality

    public bool Equals(ChatEntryId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is ChatEntryId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ChatEntryId? left, ChatEntryId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ChatEntryId? left, ChatEntryId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId chatId, ChatEntryKind kind, long localId)
        => $"{chatId.Value}:{kind.Format()}:{localId.Format()}";

    public static ChatEntryId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatEntryId>(s);

    public static ChatEntryId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ChatEntryId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ChatEntryId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var chatIdLength = s.OrdinalIndexOf(":");
        if (chatIdLength < 0)
            return false;
        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var kindStart = chatIdLength + 1;
        var kindLength = s.OrdinalIndexOf(":", kindStart);
        if (kindLength < 0)
            return false;

        var sKind = s.AsSpan(kindStart, kindLength - kindStart);
        if (!NumberExt.TryParsePositiveInt(sKind, out var kind))
            return false;
        if (kind > 2)
            return false;

        var sLocalId = s.AsSpan(kindLength + 1);
        if (!NumberExt.TryParsePositiveLong(sLocalId, out var localId))
            return false;

        result = (ChatEntryKind)kind switch {
            ChatEntryKind.Text => new TextEntryId(s, chatId, localId),
            ChatEntryKind.Audio => new AudioEntryId(s, chatId, localId),
            _ => null,
        };
        if (result == null)
            return false;

        result = Cache.AddOrGet(s, result);
        return true;
    }

    public TextEntryId ToTextEntryId()
    {
        if (this is not TextEntryId textEntryId)
            throw StandardError.Constraint($"Invalid entry kind: '{Kind}'");

        return textEntryId;
    }
}
