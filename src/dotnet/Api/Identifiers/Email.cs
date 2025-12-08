using System.ComponentModel;
using System.Net.Mail;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<Email>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<Email>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<Email>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<Email>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class Email : StringIdentifier, IStringIdentifier<Email>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<Email>();
    private static readonly ILruCache<string, Email> Cache = CreateCache<Email>(256);

    [IgnoreDataMember]
    public string Hash => field ??= ContactIdExt.Hash(Value);

    // Factories and constructors

    public static Email New(string value)
        => Parse(value);

    private Email(string value) : base(value)
    {
    }

    // Normalization

    public bool IsNormalized()
        => OrdinalEquals(Value, Value.ToLowerInvariant());

    public Email Normalize()
        => IsNormalized()
            ? this
            : New(Value.ToLowerInvariant());

    public Email RequireNormalized()
        => IsNormalized()
            ? this
            : throw StandardError.Format<Email>(Value);

    // Equality

    public bool Equals(Email? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is Email other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Email? left, Email? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Email? left, Email? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static Email Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Email>(s);

    public static Email? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static Email? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out Email? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (s.Length > 320) // Maximum email length per RFC 5321
            return false;

        if (!IsValidEmail(s))
            return false;

        // Normalize email to lowercase before caching and storing
        var normalizedEmail = s.ToLowerInvariant();

        if (Cache.TryGetValue(normalizedEmail, out var cached)) {
            result = cached;
            return true;
        }

        result = new Email(normalizedEmail);
        result = Cache.AddOrGet(normalizedEmail, result);
        return true;
    }

    // Helpers

    private static bool IsValidEmail(string email)
        => MailAddress.TryCreate(email, out var mailAddress) &&
            // Ensure that the parsed address matches the original input
            OrdinalEquals(mailAddress.Address, email);
}
