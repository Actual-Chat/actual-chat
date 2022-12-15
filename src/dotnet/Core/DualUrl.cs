namespace ActualChat;

public readonly record struct DualUrl(LocalUrl LocalUrl, string AbsoluteUrl)
{
    public string ShortLocalUrl
        => LocalUrl.Value[1..];

    public string ShortAbsoluteUrl {
        get {
            var uri = new Uri(AbsoluteUrl);
            return $"{uri.Host}{uri.AbsolutePath}";
        }
    }

    // Equality
    public bool Equals(DualUrl other) => LocalUrl.Equals(other.LocalUrl);
    public override int GetHashCode() => LocalUrl.GetHashCode();
}
