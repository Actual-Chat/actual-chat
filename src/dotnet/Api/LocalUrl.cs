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
    {
        // Drops a matching origin, then requires the remainder to be truly local
        if (!origin.IsAbsoluteUri)
            throw new ArgumentOutOfRangeException(nameof(origin));

        result = default;
        if (s.IsNullOrEmpty())
            return false;

        // UriKind.Absolute reads "/chat" as an implicit file path on Unix, so absoluteness
        // is decided via RelativeOrAbsolute, which answers the same way on every platform.
        if (Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out var uri) && uri.IsAbsoluteUri) {
            if (!string.Equals(uri.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.IdnHost, origin.IdnHost, StringComparison.OrdinalIgnoreCase)
                || uri.Port != origin.Port)
                return false;

            s = uri.PathAndQuery + uri.Fragment;
        }

        var localUrl = new LocalUrl(s);
        if (!localUrl.IsTrulyLocal())
            return false;

        result = localUrl;
        return true;
    }

    public static LocalUrl? FromAbsolute(string url, UrlMapper mapper)
    {
        var origin = mapper.BaseUri.OriginalString.TrimEnd('/');
        if (!url.StartsWith(origin))
            return null;

        var relativeUrl = url[origin.Length..];
        return relativeUrl;
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
}
