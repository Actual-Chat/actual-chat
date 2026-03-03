using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

/// <summary>
/// Unique identifier for a chat entry (message).
/// String format: "chatId:kind:localId" where kind is always 0 for text entries.
/// </summary>
#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<ChatEntryId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ChatEntryId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<ChatEntryId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ChatEntryId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class ChatEntryId : StringIdentifier, IStringIdentifier<ChatEntryId>
{
    public const string Delimiter = ":";
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatEntryId>();
    private static readonly ILruCache<string, ChatEntryId> Cache = CreateCache<ChatEntryId>(2048);

    [IgnoreDataMember]
    public ChatId ChatId { get; }
    [IgnoreDataMember]
    public long LocalId { get; }

    // Factories and constructors

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChatEntryId New(ChatId chatId, long localId)
        => new(Format(chatId, localId), chatId, localId);

    internal ChatEntryId(string value, ChatId chatId, long localId) : base(value)
    {
        ChatId = chatId;
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

    public static string Prefix(ChatId chatId)
        => Prefix(chatId.Value);

    public static string Prefix(string chatId)
        => $"{chatId}{Delimiter}";

    public static string Format(ChatId chatId, long localId)
        => $"{chatId.Value}{Delimiter}0{Delimiter}{localId.Format()}";

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

        var chatIdLength = s.OrdinalIndexOf(Delimiter);
        if (chatIdLength < 0)
            return false;
        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var kindStart = chatIdLength + 1;
        var kindLength = s.OrdinalIndexOf(Delimiter, kindStart);
        if (kindLength < 0)
            return false;

        var sKind = s.AsSpan(kindStart, kindLength - kindStart);
        if (!NumberExt.TryParsePositiveInt(sKind, out var kind))
            return false;
        // Accept kind 0 (text) and 1 (legacy audio) for backward compatibility
        if (kind > 1)
            return false;

        var sLocalId = s.AsSpan(kindLength + 1);
        if (!NumberExt.TryParsePositiveLong(sLocalId, out var localId))
            return false;

        result = new ChatEntryId(s, chatId, localId);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
