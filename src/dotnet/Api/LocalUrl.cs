using System.ComponentModel;
using ActualChat.Internal;
using Microsoft.AspNetCore.Components;

namespace ActualChat;

/// <summary>
/// Represents a normalized local URL path starting with '/'.
/// </summary>
[DataContract]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<LocalUrl>))]
[JsonConverter(typeof(StringLikeJsonConverter<LocalUrl>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<LocalUrl>))]
[TypeConverter(typeof(StringLikeTypeConverter<LocalUrl>))]
public readonly partial struct LocalUrl : IStringLike<LocalUrl>, IEquatable<LocalUrl>
{
    [DataMember, Key(0)]
    public string Value => field ?? "/";

    public static LocalUrl Parse(string? s) => new(s);

    public static bool TryParse(string? s, Uri origin, out LocalUrl result)
        => TryParse(s, origin, false, out result);

    // An app link reaches us on either scheme: the Android intent filter advertises http and
    // https alike, and Android resolves a bare host tap to http. The host still has to match,
    // so the scheme carries no authority of its own here. Absolute-only: an app link always
    // carries its origin, and a relative value would skip the host check entirely.
    public static bool TryParseAppLink(string? s, Uri origin, out LocalUrl result)
    {
        result = default;
        if (!IsAbsoluteUrl(s))
            return false;

        return TryParse(s, origin, true, out result);
    }

    public static LocalUrl? FromAbsolute(string url, UrlMapper mapper)
    {
        // Absolute-only: callers pass unvalidated text (message markup, notification payloads),
        // where a relative url names no origin to check against and must not pass as local.
        if (!IsAbsoluteUrl(url))
            return null;

        // Not a ternary: the implicit string conversion would turn a null branch into LocalUrl("/")
        if (!TryParse(url, mapper.BaseUri, out var result))
            return null;

        return result;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string DisplayText => Value.Length <= 1 ? Value : Value[1..];

    [SerializationConstructor]
    public LocalUrl(string? value)
    {
        if (value.IsNullOrEmpty()) {
            Value = "/";
            return;
        }

        if (!value.StartsWith('/'))
            value = "/" + value;
        if (value.EndsWith('/') && value.Length > 1)
            value = value[..^1];
        Value = value;
    }

    public LocalUrl(string value, ParseOrNone _)
        => Value = value;

    public override string ToString()
        => Value;

    public bool IsTrulyLocal()
    {
        // Browsers strip tab/LF/CR before parsing, and read "//host" and "/\host" as cross-origin
        var value = Value;
        if (value.AsSpan().IndexOfAny('\t', '\n', '\r') >= 0)
            return false;
        if (value[0] != '/')
            return false;

        return value.Length == 1 || value[1] is not ('/' or '\\');
    }

    public LocalUrl AssertLocal()
        => Value[0] == '/'
            ? this
            : throw StandardError.Constraint($"'{Value}' is not a local URL.");

    public LocalUrl AssertTrulyLocal()
        => IsTrulyLocal()
            ? this
            : throw StandardError.Constraint($"'{Value}' is not a truly local URL.");

    public string ToAbsolute(UrlMapper urlMapper)
        => urlMapper.ToAbsolute(this);

    public string ToAbsolute(NavigationManager nav)
        => nav.ToAbsoluteUri(Value).ToString();

    public DisplayUrl ToDisplayUrl(UrlMapper urlMapper)
        => new(this, ToAbsolute(urlMapper));

    public DisplayUrl ToDisplayUrl(NavigationManager nav)
        => new(this, ToAbsolute(nav));

    public static implicit operator LocalUrl(string url) => new(url);
    public static implicit operator string(LocalUrl localUrl) => localUrl.Value;

    // Equality
    public bool Equals(LocalUrl other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is LocalUrl other && Equals(other);
    public override int GetHashCode() => Value.GetOrdinalHashCode();
    public static bool operator ==(LocalUrl left, LocalUrl right) => left.Equals(right);
    public static bool operator !=(LocalUrl left, LocalUrl right) => !left.Equals(right);

    // Handy operators

    public static LocalUrl operator +(LocalUrl left, string right)
        => new(left.Value + right);

    // Private methods

    private static bool TryParse(string? s, Uri origin, bool isAnyWebSchemeAllowed, out LocalUrl result)
    {
        // Drops a matching origin, then requires the remainder to be truly local
        if (!origin.IsAbsoluteUri)
            throw new ArgumentOutOfRangeException(nameof(origin));

        result = default;
        if (s.IsNullOrEmpty())
            return false;

        if (Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out var uri) && uri.IsAbsoluteUri) {
            if (!IsSameOrigin(uri, origin, isAnyWebSchemeAllowed))
                return false;

            s = uri.PathAndQuery + uri.Fragment;
        }

        var localUrl = new LocalUrl(s);
        if (!localUrl.IsTrulyLocal())
            return false;

        result = localUrl;
        return true;
    }

    private static bool IsSameOrigin(Uri uri, Uri origin, bool isAnyWebSchemeAllowed)
    {
        if (!string.Equals(uri.IdnHost, origin.IdnHost, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(uri.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase))
            return uri.Port == origin.Port;
        if (!isAnyWebSchemeAllowed)
            return false;

        // http and https carry different default ports, so a cross-scheme match holds only when
        // neither side names one - an explicit port belongs to a single scheme.
        return IsWebScheme(uri.Scheme) && IsWebScheme(origin.Scheme)
            && uri.IsDefaultPort && origin.IsDefaultPort;
    }

    private static bool IsWebScheme(string scheme)
        => string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool IsAbsoluteUrl(string? s)
        // UriKind.Absolute reads "/chat" as an implicit file path on Unix, so absoluteness is
        // decided via RelativeOrAbsolute, which answers the same way on every platform.
        => Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out var uri) && uri.IsAbsoluteUri;
}
