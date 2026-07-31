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

        // RPC transport. Android runs on HTTP/2 while we isolate a suspected WebSocket
        // regression. It's a runtime check rather than a define because Api is built once and
        // shared by every platform head, so a per-TFM DefineConstants would never reach it.
        public static readonly bool UseHttpClient =
#if USE_RPC_HTTP_CLIENT
            true;
#else
            OSInfo.IsAndroid;
#endif
    }
}
