using ActualLab.OS;

namespace ActualChat;

public static partial class Constants
{
    public static class Rpc
    {
        public static class RemoteComputedCache
        {
            public static readonly TimeSpan HitToCallInitialDelay = TimeSpan.FromMilliseconds(1500);
        }

        public static class Compression
        {
            public const bool IsServerSideEnabled = true;
            public const bool IsClientSideEnabled = true;
        }

        // Forces HTTP/2 transport for RPC
        public static readonly bool UseHttpClient =
#if USE_RPC_HTTP_CLIENT
            true;
#else
            // Android runs on HTTP/2 while we isolate a suspected
            // WebSocket regression on .NET 11 & MAUI Android.
            OSInfo.IsAndroid;
#endif
    }
}
