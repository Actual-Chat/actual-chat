namespace ActualChat;

public static class UriExt
{
    public static string WithoutFragment(this Uri uri)
        => uri.Fragment.IsNullOrEmpty()
            ? uri.AbsoluteUri
            : uri.AbsoluteUri[..uri.AbsoluteUri.OrdinalLastIndexOf(uri.Fragment)];
}
