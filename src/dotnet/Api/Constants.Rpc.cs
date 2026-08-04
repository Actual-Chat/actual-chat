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
            public static readonly bool IsServerSideEnabled = false; // .NET 11 issue: automatic 30s keep-alives don't work w/ compression
            public static readonly bool IsClientSideEnabled = false; // .NET 11 issue: automatic 30s keep-alives don't work w/ compression
        }

        // RPC transport
        public static readonly bool UseHttpClient =
#if USE_RPC_HTTP_CLIENT
            true;
#else
            false;
#endif
    }
}
