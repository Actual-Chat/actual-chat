using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<PlaceChatId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<PlaceChatId2>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<PlaceChatId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<PlaceChatId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class PlaceChatId2 : ChatId2, IStringIdentifier<PlaceChatId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PlaceChatId2>();

    public static readonly string IdPrefix = "s-";

    [IgnoreDataMember]
    public PlaceId2 PlaceId { get; }
    [IgnoreDataMember]
    public string LocalChatId { get; }
    [IgnoreDataMember]
    public bool IsRoot { get; }

    // Factories and constructors

    public static PlaceChatId2 New(PlaceId2 placeId)
    {
        var localChatId = IdGenerator.Next();
        return new(Format(placeId, localChatId), placeId, localChatId, false);
    }

    internal PlaceChatId2(string value, PlaceId2 placeId, string localChatId)
        : base(value, ChatKind.Place)
    {
        PlaceId = placeId;
        LocalChatId = localChatId;
        IsRoot = string.Equals(placeId.Value, localChatId, StringComparison.Ordinal);
    }

    private PlaceChatId2(string value, PlaceId2 placeId, string localChatId, bool isRoot)
        : base(value, ChatKind.Place)
    {
        PlaceId = placeId;
        LocalChatId = localChatId;
    }

    // Equality

    public bool Equals(PlaceChatId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is PlaceChatId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(PlaceChatId2? left, PlaceChatId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(PlaceChatId2? left, PlaceChatId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(PlaceId2 placeId, string localChatId)
        => $"{IdPrefix}{placeId}-{localChatId}";

    public static new PlaceChatId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PlaceChatId2>(s);

    public static new PlaceChatId2? ParseOrNull(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static new PlaceChatId2? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out PlaceChatId2? result)
    {
        if (!ChatId2.TryParse(s, out var chatId)) {
            result = null;
            return false;
        }

        result = chatId as PlaceChatId2;
        return result is not null;
    }
}
