using System.ComponentModel;
using ActualChat.Internal;
using Microsoft.AspNetCore.Components;

namespace ActualChat;

/// <summary>
/// Represents a normalized local URL path starting with '/'.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// MemoryPack wire format intentionally kept SG-generated (IMemoryPackable<T> map) to stay
// compatible with older clients. Switch to plain-string when safe by uncommenting:
// [MemoryPackFormatter<StringLikeMemoryPackFormatter<LocalUrl>>]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<LocalUrl>))]
[JsonConverter(typeof(StringLikeJsonConverter<LocalUrl>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<LocalUrl>))]
[TypeConverter(typeof(StringLikeTypeConverter<LocalUrl>))]
public readonly partial struct LocalUrl : IStringLike<LocalUrl>, IEquatable<LocalUrl>
{
    [DataMember, MemoryPackOrder(0)]
    public string Value => field ?? "/";

    public static LocalUrl Parse(string? s) => new(s);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string DisplayText => Value.Length <= 1 ? Value : Value[1..];

    [MemoryPackConstructor, SerializationConstructor]
    public LocalUrl(string? value)
    {
        // Normalizing it
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
