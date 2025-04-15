using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<TextEntryId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<TextEntryId2>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<TextEntryId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<TextEntryId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class TextEntryId2 : ChatEntryId2, IStringIdentifier<TextEntryId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<TextEntryId2>();

    // Factories and constructors

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TextEntryId2 New(ChatId2 chatId, long localId)
        => new(Format(chatId, ChatEntryKind.Text, localId), chatId, localId);

    internal TextEntryId2(string value, ChatId2 chatId, long localId)
        : base(value, chatId, ChatEntryKind.Text, localId)
    { }

    // Equality

    public bool Equals(TextEntryId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is TextEntryId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TextEntryId2? left, TextEntryId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TextEntryId2? left, TextEntryId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static new TextEntryId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<TextEntryId2>(s);

    public static TextEntryId2? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out TextEntryId2? result)
    {
        if (!ChatEntryId2.TryParse(s, out var chatEntryId)) {
            result = null;
            return false;
        }

        result = chatEntryId as TextEntryId2;
        return result is not null;
    }
}
