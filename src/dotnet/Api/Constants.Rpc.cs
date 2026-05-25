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
            public const bool IsServerSideEnabled = false;
            public const bool IsClientSideEnabled = true;
        }

        // RPC transport
        public const bool UseHttpClient =
#if USE_RPC_HTTP_CLIENT
            true;
#else
            false;
#endif
    }
}
