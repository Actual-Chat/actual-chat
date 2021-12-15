using Cysharp.Text;

namespace ActualChat.Blobs;

public static class BlobPath
{
    public static readonly char ScopeDelimiter = '/';

    public static string Format(Symbol scope, string scopedId)
        => ZString.Concat(scope.Value, ScopeDelimiter, scopedId);
    public static string Format(Symbol scope, params string[] parts)
        => ZString.Concat(scope.Value, ScopeDelimiter, ZString.Join(ScopeDelimiter, parts));

    public static string GetScope(string blobId)
    {
        var scopeDelimiterIndex = blobId.IndexOf(ScopeDelimiter);
        return scopeDelimiterIndex <= 0 ? "" : blobId[..scopeDelimiterIndex];
    }
}
