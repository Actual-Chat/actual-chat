using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<RoleId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<RoleId2>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<RoleId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<RoleId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class RoleId2 : StringIdentifier, IStringIdentifier<RoleId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<RoleId2>();
    private static readonly ILruCache<string, RoleId2> Cache = CreateCache<RoleId2>(128);

    public const char Delimiter = ':';

    [IgnoreDataMember]
    public ChatId2 ChatId { get; }
    [IgnoreDataMember]
    public long LocalId { get; }

    // Factories and constructors

    public static RoleId2 New(ChatId2 chatId, long localId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(localId);
        return new(Format(chatId, localId), chatId, localId);
    }

    private RoleId2(string value, ChatId2 chatId, long localId) : base(value)
    {
        ChatId = chatId;
        LocalId = localId;
    }

    // Equality

    public bool Equals(RoleId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is RoleId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(RoleId2? left, RoleId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(RoleId2? left, RoleId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId2 chatId, long localId)
        => $"{chatId.Value}{Delimiter}{localId.Format()}";

    public static RoleId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<RoleId2>(s);

    public static RoleId2? ParseOrNull(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static RoleId2? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out RoleId2? result)
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
        if (!NumberExt.TryParsePositiveLong(s.AsSpan(chatIdLength + 1), out var localId))
            return false;

        result = new RoleId2(s, chatId, localId);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
