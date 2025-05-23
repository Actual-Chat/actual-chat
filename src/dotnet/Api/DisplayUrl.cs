namespace ActualChat;

public readonly record struct DisplayUrl(LocalUrl LocalUrl, string AbsoluteUrl)
{
    public string ShortLocalUrl
        => LocalUrl.DisplayText;

    public string ShortAbsoluteUrl {
        get {
            var uri = AbsoluteUrl.ToUri();
            return $"{uri.Host}{uri.AbsolutePath}";
        }
    }

    // Equality
    public bool Equals(DisplayUrl other) => LocalUrl.Equals(other.LocalUrl);
    public override int GetHashCode() => LocalUrl.GetHashCode();
}
