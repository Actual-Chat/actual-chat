using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<AudioEntryId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<AudioEntryId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<AudioEntryId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class AudioEntryId(string value, ChatId2 chatId, long localId, AssumeValid _)
    : ChatEntryId2(value, chatId, ChatEntryKind.Text, localId, AssumeValid.Option), IStringIdentifier<AudioEntryId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<AudioEntryId>();

    // Factories

    public static AudioEntryId New(ChatId2 chatId, long localId)
        => new(Format(chatId, ChatEntryKind.Text, localId), chatId, localId, AssumeValid.Option);

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
