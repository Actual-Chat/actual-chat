using System.ComponentModel;
using ActualChat.Internal;
using Microsoft.AspNetCore.Components;

namespace ActualChat;

/// <summary>
/// Represents a normalized local URL path starting with '/'.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<LocalUrl>))]
[JsonConverter(typeof(StringLikeJsonConverter<LocalUrl>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<LocalUrl>))]
[TypeConverter(typeof(StringLikeTypeConverter<LocalUrl>))]
public readonly partial struct LocalUrl : IStringLike<LocalUrl>, IEquatable<LocalUrl>
{
    [DataMember, MemoryPackOrder(0), Key(0)]
    public string Value => field ?? "/";

    public static LocalUrl Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<LocalUrl>(s);

    public static bool TryParse(string? s, out LocalUrl result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true;

        if (Uri.TryCreate(s, UriKind.Absolute, out _))
            return false;

        if (!s.StartsWith('/'))
            s = "/" + s;
        if (s.EndsWith('/') && s.Length > 1)
            s = s[..^1];
        if (!Uri.TryCreate(s, UriKind.Relative, out _))
            return false;

        result = new LocalUrl(s, ParseOrNone.Option);
        return true;
    }

    public static bool TryParse(string? s, Uri origin, out LocalUrl result)
    {
        if (!origin.IsAbsoluteUri)
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!Uri.TryCreate(s, UriKind.Absolute, out var absoluteUrl))
            return TryParse(s, out result);

        result = default;
        if (!string.Equals(absoluteUrl.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(absoluteUrl.IdnHost, origin.IdnHost, StringComparison.OrdinalIgnoreCase)
            || absoluteUrl.Port != origin.Port)
            return false;

        return TryParse(absoluteUrl.PathAndQuery + absoluteUrl.Fragment, out result);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string DisplayText => Value.Length <= 1 ? Value : Value[1..];

    [MemoryPackConstructor, SerializationConstructor]
    public LocalUrl(string? value)
    {
        if (!TryParse(value, out var result))
            throw StandardError.Format<LocalUrl>(value);
        Value = result.Value;
    }

    public LocalUrl(string value, ParseOrNone _)
        => Value = value;

    public override string ToString()
        => Value;

    public static LocalUrl? FromAbsolute(string url, UrlMapper mapper)
    {
        var origin = mapper.BaseUri.OriginalString.TrimEnd('/');
        if (!url.StartsWith(origin))
            return null;

        var relativeUrl = url[origin.Length..];
        return relativeUrl;
    }

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
