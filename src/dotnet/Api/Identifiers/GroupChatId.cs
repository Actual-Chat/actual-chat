using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for a group chat with multiple participants.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<GroupChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<GroupChatId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<GroupChatId>))]
[TypeConverter(typeof(StringLikeTypeConverter<GroupChatId>))]
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

    internal GroupChatId(string value)
        : base(value, ChatKind.Group)
    { }

    // Equality

    public bool Equals(GroupChatId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
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
