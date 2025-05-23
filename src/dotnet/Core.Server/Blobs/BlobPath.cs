namespace ActualChat.Blobs;

public static class BlobPath
{
    public static readonly char ScopeDelimiterChar = '/';
    public static readonly string ScopeDelimiter = ScopeDelimiterChar.ToString();

    public static string Format(Symbol scope, string scopedId)
        => string.Concat(scope.Value, ScopeDelimiter, scopedId);
    public static string Format(Symbol scope, params string[] parts)
        => string.Concat(scope.Value, ScopeDelimiter, string.Join(ScopeDelimiter, parts));

    public static string GetScope(string blobId)
    {
        var scopeDelimiterIndex = blobId.OrdinalIndexOf(ScopeDelimiterChar);
        return scopeDelimiterIndex <= 0 ? "" : blobId[..scopeDelimiterIndex];
    }
}
