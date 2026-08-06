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

        // RPC transport: index of the RpcSwitchingClient's client to connect with first.
        // 0 is WebSocket, 1 is HTTP; it rotates to the next one on its own if that one can't hold a connection.
        public static readonly int SwitchingClientStartIndex = 0;
    }
}
