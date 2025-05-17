using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<TextEntryId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<TextEntryId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<TextEntryId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<TextEntryId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class TextEntryId : ChatEntryId, IStringIdentifier<TextEntryId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<TextEntryId>();

    // Factories and constructors

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TextEntryId New(ChatId chatId, long localId)
        => new(Format(chatId, ChatEntryKind.Text, localId), chatId, localId);

    internal TextEntryId(string value, ChatId chatId, long localId)
        : base(value, chatId, ChatEntryKind.Text, localId)
    { }

    // Equality

    public bool Equals(TextEntryId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is TextEntryId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TextEntryId? left, TextEntryId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TextEntryId? left, TextEntryId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static new TextEntryId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<TextEntryId>(s);

    public static new TextEntryId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static new TextEntryId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out TextEntryId? result)
    {
        if (!ChatEntryId.TryParse(s, out var chatEntryId)) {
            result = null;
            return false;
        }

        result = chatEntryId as TextEntryId;
        return result is not null;
    }
}
