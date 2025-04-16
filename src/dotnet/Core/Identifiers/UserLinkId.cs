using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<UserLinkId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<UserLinkId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<UserLinkId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<UserLinkId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class UserLinkId : StringIdentifier, IStringIdentifier<UserLinkId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<UserLinkId>();
    private static readonly ILruCache<string, UserLinkId> Cache = CreateCache<UserLinkId>(256);

    public static readonly Alphabet Alphabet = Alphabet.AlphaNumeric.Symbols + "_-";

    [IgnoreDataMember] [field: AllowNull, MaybeNull]
    public string NormalizedValue => field ??= Value.ToLowerInvariant();


    // Factories and constructors

    private UserLinkId(string value) : base(value)
    { }

    // Equality

    public bool Equals(UserLinkId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is UserLinkId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UserLinkId? left, UserLinkId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UserLinkId? left, UserLinkId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static UserLinkId Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UserLinkId>(s);

    public static UserLinkId? ParseOrNull(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static UserLinkId? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out UserLinkId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length < 5 || !Alphabet.IsMatch(s))
            return false;

        result = new UserLinkId(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
