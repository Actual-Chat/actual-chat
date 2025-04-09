using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<UserId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<UserId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<UserId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class UserId2(string value, AssumeValid _)
    : StringIdentifier(value), IStringIdentifier<UserId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<UserId>();
    private static readonly ILruCache<string, UserId2> Cache = CreateCache<UserId2>(256);
    private static readonly RandomStringGenerator IdGenerator = new(6, Alphabet.AlphaNumeric);
    private static readonly RandomStringGenerator GuestIdGenerator = new(8, Alphabet.AlphaNumeric);

    public static readonly Comparer<UserId2> Comparer = Comparer<UserId2>.Default;
    public static readonly char GuestIdPrefixChar = '~';

    [IgnoreDataMember]
    public bool IsGuest => Value.Length != 0 && Value[0] == GuestIdPrefixChar;

    // Factories

    public static UserId2 New()
        => new(IdGenerator.Next(), AssumeValid.Option);

    public static UserId2 NewGuest()
        => new(GuestIdPrefixChar + GuestIdGenerator.Next(), AssumeValid.Option);

    // Equality

    public bool Equals(UserId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is UserId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UserId2? left, UserId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UserId2? left, UserId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static UserId2 Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UserId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out UserId2? result)
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

        result = new UserId2(s, AssumeValid.Option);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}

public static class UserId2Ext
{
    public static bool IsGuestOrNull([NotNullWhen(false)] this UserId2? userId)
        => userId == null || userId.IsGuest;
}
