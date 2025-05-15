using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<PlaceChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<PlaceChatId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<PlaceChatId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<PlaceChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class PlaceChatId : ChatId, IStringIdentifier<PlaceChatId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PlaceChatId>();

    public static readonly string IdPrefix = "s-";

    private readonly LocalChatId _localChatId;

    [IgnoreDataMember]
    public PlaceId PlaceId { get; }

    [IgnoreDataMember]
    public string LocalChatId => _localChatId.Id;
    [IgnoreDataMember]
    public bool IsRoot { get; }
    [IgnoreDataMember]
    [field: AllowNull, MaybeNull]
    public PlaceChatId RootChatId => field ??= IsRoot ? this : Parse(Format(PlaceId, PlaceId.Value));

    // Factories and constructors

    public static PlaceChatId New(PlaceId placeId)
    {
        var localChatId = IdGenerator.Next();
        return new(Format(placeId, localChatId), placeId, ActualChat.LocalChatId.New(localChatId), false);
    }

    internal PlaceChatId(string value, PlaceId placeId, LocalChatId localChatId)
        : this(value, placeId, localChatId, OrdinalEquals(placeId.Value, localChatId.Id))
    { }

    private PlaceChatId(string value, PlaceId placeId, LocalChatId localChatId, bool isRoot)
        : base(value, ChatKind.Place)
    {
        PlaceId = placeId;
        _localChatId = localChatId;
        IsRoot = isRoot;
    }

    // Equality

    public bool Equals(PlaceChatId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
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

    // Threads

    public override long ThreadId => _localChatId.ThreadId;
    public new PlaceChatId GetThreadParentOrSelf()
    {
        if (!IsThread)
            return this;

        var parentGroupChat = _localChatId.Parent;
        return new PlaceChatId(Format(PlaceId, parentGroupChat!.Id),
            PlaceId,
            parentGroupChat);
    }
}
