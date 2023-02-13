using Microsoft.AspNetCore.Components;

namespace ActualChat;

[DataContract]
public readonly struct LocalUrl : IEquatable<LocalUrl>
{
    private readonly string _value;

    [DataMember]
    public string Value => _value ?? "/";

    public LocalUrl(string? value)
    {
        // Normalizing it
        if (value.IsNullOrEmpty()) {
            _value = "/";
            return;
        }
        if (!value.OrdinalStartsWith("/"))
            value = "/" + value;
        if (value.OrdinalEndsWith("/"))
            value = value[..^1];
        _value = value;
    }

    public LocalUrl(string value, ParseOrNone _)
        => _value = value;

    public override string ToString()
        => Value;

    public string ToAbsolute(UrlMapper urlMapper)
        => urlMapper.ToAbsolute(this);
    public string ToAbsolute(NavigationManager nav)
        => nav.ToAbsoluteUri(Value).ToString();

    public DisplayUrl ToDisplayUrl(UrlMapper urlMapper)
        => new (this, ToAbsolute(urlMapper));
    public DisplayUrl ToDisplayUrl(NavigationManager nav)
        => new (this, ToAbsolute(nav));

    public static implicit operator LocalUrl(string url) => new (url);
    public static implicit operator string(LocalUrl localUrl) => localUrl.Value;

    // Equality
    public bool Equals(LocalUrl other) => OrdinalEquals(Value, other.Value);
    public override bool Equals(object? obj) => obj is LocalUrl other && Equals(other);
    public override int GetHashCode() => Value.OrdinalHashCode();
    public static bool operator ==(LocalUrl left, LocalUrl right) => left.Equals(right);
    public static bool operator !=(LocalUrl left, LocalUrl right) => !left.Equals(right);
}
