using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<UserLinkId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<UserLinkId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<UserLinkId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class UserLinkId2(string value, AssumeValid _)
    : StringIdentifier(value), IStringIdentifier<UserLinkId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<UserLinkId2>();
    private static readonly ILruCache<string, UserLinkId2> Cache = CreateCache<UserLinkId2>(256);

    public static readonly Alphabet Alphabet = Alphabet.AlphaNumeric.Symbols + "_-";

    [IgnoreDataMember] [field: AllowNull, MaybeNull]
    public string NormalizedValue => field ??= Value.ToLowerInvariant();

    // Equality

    public bool Equals(UserLinkId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is UserLinkId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UserLinkId2? left, UserLinkId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UserLinkId2? left, UserLinkId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static UserLinkId2 Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UserLinkId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out UserLinkId2? result)
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

        result = new UserLinkId2(s, AssumeValid.Option);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
