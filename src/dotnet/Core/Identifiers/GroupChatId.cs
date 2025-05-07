using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<GroupChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<GroupChatId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<GroupChatId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<GroupChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class GroupChatId : ChatId, IStringIdentifier<GroupChatId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<GroupChatId>();

    // Factories and constructors

    public static GroupChatId New()
    {
        var localChatId = IdGenerator.Next();
        return new(localChatId);
    }

    internal GroupChatId(string value) : base(value, ChatKind.Place)
    { }

    // Equality

    public bool Equals(GroupChatId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is GroupChatId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(GroupChatId? left, GroupChatId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(GroupChatId? left, GroupChatId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static new GroupChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<GroupChatId>(s);

    public static new GroupChatId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static new GroupChatId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out GroupChatId? result)
    {
        if (!ChatId.TryParse(s, out var chatId)) {
            result = null;
            return false;
        }

        result = chatId as GroupChatId;
        return result is not null;
    }
}
