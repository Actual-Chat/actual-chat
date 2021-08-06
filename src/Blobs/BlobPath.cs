using Cysharp.Text;
using Stl.Text;

namespace ActualChat.Blobs
{
    public class BlobId
    {
        public static char ScopeDelimiter = '/';

        public static string Format(Symbol scope, string scopedId)
            => ZString.Concat(scope.Value, '/', scopedId);

        public static string GetScope(string blobId)
        {
            var scopeDelimiterIndex = blobId.IndexOf(ScopeDelimiter);
            return scopeDelimiterIndex <= 0 ? "" : blobId[..scopeDelimiterIndex];
        }
    }
}
