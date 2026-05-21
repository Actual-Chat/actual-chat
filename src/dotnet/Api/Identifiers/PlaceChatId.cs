using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for a chat within a <see cref="Place"/>.
/// </summary>
[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringLikeJsonConverter<PlaceChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<PlaceChatId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<PlaceChatId>))]
[TypeConverter(typeof(StringLikeTypeConverter<PlaceChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class PlaceChatId : ChatId, IStringIdentifier<PlaceChatId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PlaceChatId>();

    public static readonly string IdPrefix = "s-";

    [IgnoreDataMember]
    public PlaceId PlaceId { get; }

    [IgnoreDataMember]
    public string LocalChatId { get; }
    [IgnoreDataMember]
    public bool IsRoot { get; }
    [IgnoreDataMember]
    public new PlaceChatId RootChatId
        => field ??= IsRoot ? this : Parse(Format(PlaceId, PlaceId.Value));

    // Factories and constructors

    public static PlaceChatId New(PlaceId placeId)
    {
        var localChatId = IdGenerator.Next();
        return new(Format(placeId, localChatId), placeId, Internal.ParsedLocalChatId.New(localChatId), false);
    }

    internal PlaceChatId(string value, PlaceId placeId, ParsedLocalChatId localChatId)
        : this(value, placeId, localChatId, placeId.Value == localChatId.Id)
    { }

    private PlaceChatId(string value, PlaceId placeId, ParsedLocalChatId localChatId, bool isRoot)
        : base(value, ChatKind.Place)
    {
        PlaceId = placeId;
        LocalChatId = localChatId.Id;
        IsRoot = isRoot;
    }

    // Equality

    public bool Equals(PlaceChatId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is PlaceChatId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(PlaceChatId? left, PlaceChatId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(PlaceChatId? left, PlaceChatId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(PlaceId placeId, string localChatId)
        => $"{IdPrefix}{placeId}-{localChatId}";

    public static new PlaceChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PlaceChatId>(s);

    public static new PlaceChatId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static new PlaceChatId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out PlaceChatId? result)
    {
        if (!ChatId.TryParse(s, out var chatId)) {
            result = null;
            return false;
        }

        result = chatId as PlaceChatId;
        return result is not null;
    }
}
