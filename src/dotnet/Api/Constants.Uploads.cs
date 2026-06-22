namespace ActualChat;

public static partial class Constants
{
    public static class Uploads
    {
        // Streaming uploads (RpcStream-based). Data is pushed as many small
        // sub-chunks so the WebSocket writer yields between items and keep-alive
        // can interleave on slow links (instead of one big Uploads_Append
        // monopolizing the client->server stream).
        public const int SubChunkSize = 16 * 1024; // 16 KB per RpcStream item

        // RpcStream flow control: client->server upload, must not drop data.
        public const int RpcStreamAckPeriod = 16; // ack every 16 sub-chunks (~256 KB)
        public const int RpcStreamAckAdvance = 64; // 64 sub-chunks credit window (~1 MB)

        // Server-side: accumulate sub-chunks and forward to the backend in
        // blocks. Flush when the buffer reaches FlushSize, or — to bound how
        // long data sits unpersisted and to keep the resume offset moving on
        // slow links — at least every MaxFlushInterval (whatever 256 KB-aligned
        // amount has accumulated by then). GCS resumable upload requires
        // non-final chunks to be a multiple of 256 KB.
        public const int StorageBlockAlignment = 256 * 1024; // 256 KB
        public const int FlushSize = 1024 * 1024; // 1 MB = 4 x 256 KB
        public static readonly TimeSpan MaxFlushInterval = TimeSpan.FromSeconds(30);
    }
}
