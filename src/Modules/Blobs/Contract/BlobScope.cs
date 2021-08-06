using Cysharp.Text;
using Stl.Text;

namespace ActualChat.Blobs
{
    public static class BlobScope
    {
        public static Symbol UserAudio = nameof(UserAudio);

        public static Symbol Get(string blobId)
        {
            var slashIndex = blobId.IndexOf('/');
            if (slashIndex <= 0)
                return Symbol.Empty;
            return blobId[..slashIndex];
        }

        public static string Format(Symbol blobScope, string partialBlobId)
            => ZString.Concat(blobScope.Value, '/', partialBlobId);
    }
}
