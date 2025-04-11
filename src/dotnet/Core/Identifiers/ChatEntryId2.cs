using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<ChatEntryId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ChatEntryId2>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<ChatEntryId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ChatEntryId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
// This type is technically abstract, but we don't want to make it abstract,
// coz this forces RPC to use polymorphic serialization for this type.
public partial class ChatEntryId2 : StringIdentifier, IStringIdentifier<ChatEntryId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatEntryId2>();
    private static readonly ILruCache<string, ChatEntryId2> Cache = CreateCache<ChatEntryId2>(2048);

    [IgnoreDataMember]
    public ChatId2 ChatId { get; }
    [IgnoreDataMember]
    public ChatEntryKind Kind { get; }
    [IgnoreDataMember]
    public long LocalId { get; }

    // Factories and constructors

    public static ChatEntryId2 New(ChatId2 chatId, ChatEntryKind kind, long localId)
        => kind switch {
            ChatEntryKind.Text => TextEntryId2.New(chatId, localId),
            ChatEntryKind.Audio => AudioEntryId.New(chatId, localId),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    protected ChatEntryId2(string value, ChatId2 chatId, ChatEntryKind kind, long localId) : base(value)
    {
        ChatId = chatId;
        Kind = kind;
        LocalId = localId;
    }

    // Equality

    public bool Equals(ChatEntryId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is ChatEntryId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ChatEntryId2? left, ChatEntryId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ChatEntryId2? left, ChatEntryId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId2 chatId, ChatEntryKind kind, long localId)
        => $"{chatId.Value}:{kind.Format()}:{localId.Format()}";

    public static ChatEntryId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatEntryId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out ChatEntryId2? result)
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
        if (!ChatId2.TryParse(s[..chatIdLength], out var chatId))
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
            ChatEntryKind.Text => new TextEntryId2(s, chatId, localId),
            ChatEntryKind.Audio => new AudioEntryId(s, chatId, localId),
            _ => null,
        };
        if (result == null)
            return false;

        result = Cache.AddOrGet(s, result);
        return true;
    }
}
