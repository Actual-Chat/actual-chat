using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<AudioEntryId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<AudioEntryId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<AudioEntryId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<AudioEntryId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class AudioEntryId : ChatEntryId2, IStringIdentifier<AudioEntryId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<AudioEntryId>();

    // Factories and constructors

    public static AudioEntryId New(ChatId2 chatId, long localId)
        => new(Format(chatId, ChatEntryKind.Text, localId), chatId, localId);

    internal AudioEntryId(string value, ChatId2 chatId, long localId)
        : base(value, chatId, ChatEntryKind.Text, localId)
    { }

    // Equality

    public bool Equals(AudioEntryId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is AudioEntryId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(AudioEntryId? left, AudioEntryId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(AudioEntryId? left, AudioEntryId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static new AudioEntryId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<AudioEntryId>(s);

    public static AudioEntryId? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out AudioEntryId? result)
    {
        if (!ChatEntryId2.TryParse(s, out var chatEntryId)) {
            result = null;
            return false;
        }

        result = chatEntryId as AudioEntryId;
        return result is not null;
    }
}
