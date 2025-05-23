using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<ChatRouletteId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ChatRouletteId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<ChatRouletteId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ChatRouletteId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class ChatRouletteId : StringIdentifier, IStringIdentifier<ChatRouletteId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatRouletteId>();
    private static readonly ILruCache<string, ChatRouletteId> Cache = CreateCache<ChatRouletteId>(512);
    private static readonly IComparer<Symbol> ProfileIdComparer = Comparer<Symbol>.Default;

    [IgnoreDataMember]
    public Symbol ProfileId1 { get; }
    [IgnoreDataMember]
    public Symbol ProfileId2 { get; }

    // Factories and constructors

    public static ChatRouletteId New(Symbol profileId1, Symbol profileId2)
    {
        if (profileId1.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(profileId1));
        if (profileId2.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(profileId2));
        if (profileId1 == profileId2)
            throw new ArgumentOutOfRangeException(nameof(profileId2), "Both profile IDs are identical.");

        (profileId1, profileId2) = (profileId1, profileId2).Sort(ProfileIdComparer);
        var id = Format(profileId1.Value, profileId2.Value);
        return new(id, profileId1, profileId2);
    }

    private ChatRouletteId(string value, Symbol profileId1, Symbol profileId2)
        : base(value)
    {
        ProfileId1 = profileId1;
        ProfileId2 = profileId2;
    }

    // Equality

    public bool Equals(ChatRouletteId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is ChatRouletteId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ChatRouletteId? left, ChatRouletteId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ChatRouletteId? left, ChatRouletteId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(string profileId1, string profileId2)
        => $"{profileId1}:{profileId2}";

    public static string Format(UserId ownerId, ChatId chatId)
        => $"{ownerId.Value} {chatId.Value}";

    public static ChatRouletteId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatRouletteId>(s);

    public static ChatRouletteId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ChatRouletteId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ChatRouletteId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var profileId1Length = s.IndexOf(':');
        if (profileId1Length < 0)
            return false;

        var profileId1 = (Symbol)s[..profileId1Length];
        var profileId2 = (Symbol)s[(profileId1Length + 1)..];
        if (profileId1.IsEmpty || profileId2.IsEmpty)
            return false;
        if (ProfileIdComparer.Compare(profileId1, profileId2) >= 0)
            return false; // Wrong sort order or they are identical

        result = new ChatRouletteId(s, profileId1, profileId2);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
