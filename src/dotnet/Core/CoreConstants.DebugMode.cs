namespace ActualChat;

// ReSharper disable MemberHidesStaticFromOuterClass

public static partial class CoreConstants
{
    public static class DebugMode
    {
        // Rpc calls
        public static class RpcCalls
        {
            public static readonly bool ApiClient = false;
            public static readonly bool ApiServer = false;
            public static readonly bool BackendClient = false;
            public static readonly bool BackendServer = false;
            public static readonly RandomTimeSpan? AnyServerInboundDelay = null; // new(0.5, 0.1);
            public static readonly bool LogExistingCacheEntryUpdates = true; // Used only in WASM + DEBUG
        }

        // Core components
        public static readonly bool SignalR = false;
        public static readonly bool StoredState = false;
        public static readonly bool SyncedState = false;
        public static readonly bool MessageProcessor = false;
    }
}
