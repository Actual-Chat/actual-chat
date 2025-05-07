using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<UserId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<UserId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<UserId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<UserId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class UserId : PrincipalId, IStringIdentifier<UserId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<UserId>();
    private static readonly ILruCache<string, UserId> Cache = CreateCache<UserId>(256);

    public static readonly RandomStringGenerator IdGenerator = new(6, Alphabet.AlphaNumeric);
    public static readonly RandomStringGenerator GuestIdGenerator = new(8, Alphabet.AlphaNumeric);
    public static readonly Comparer<UserId> Comparer = Comparer<UserId>.Default;
    public static readonly char GuestIdPrefixChar = '~';

    [IgnoreDataMember]
    public bool IsGuest => Value.Length != 0 && Value[0] == GuestIdPrefixChar;

    // Factories and constructors

    public static UserId New()
        => new(IdGenerator.Next());

    public static UserId NewGuest()
        => new(GuestIdPrefixChar + GuestIdGenerator.Next());

    private UserId(string value) : base(value, PrincipalKind.User)
    { }

    // Equality

    public bool Equals(UserId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is UserId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UserId? left, UserId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UserId? left, UserId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static new UserId Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UserId>(s);

    public static new UserId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static new UserId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out UserId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length is < 3 or > 64) // Tests may use some accounts with short Ids + there is "admin"
            return false;

        var alphabet = Alphabet.AlphaNumericDash;
        for (var i = 0; i < s.Length; i++) {
            var c = s[i];
            if (!alphabet.IsMatch(c)) {
                if (c == GuestIdPrefixChar && i == 0)
                    continue; // GuestId
                return false;
            }
        }

        result = new UserId(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}

public static class UserIdExt
{
    public static bool IsGuestOrNull([NotNullWhen(false)] this UserId? userId)
        => userId is null || userId.IsGuest;
}
