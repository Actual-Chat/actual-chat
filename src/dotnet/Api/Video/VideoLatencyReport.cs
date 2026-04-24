namespace ActualChat.Video;

// Per-peer video latency report pushed from client to server via
// IStreamServer.ReportVideoLatency. Carried as a single record so the RPC
// signature stays stable as new metrics are added — append a new property
// with the next MemoryPackOrder and VersionTolerant handles the wire.
//
// Semantics of sentinel values on the numeric fields (preserved from the
// previous positional-args shape; `StreamLatencyStore` treats -1 as "no
// sample this tick"):
//   - MedianDecodeTimeMs = -1 → no decode sample available (e.g. worker
//     hasn't produced stats yet after a reset).
//   - BufferDepth = -1 → no buffer measurement.
//   - BufferSpanMs = -1 → no buffer-span measurement.
//   - RenderQuality = null → no render-size hint from the client this tick;
//     server applies no render-hint cap. Non-null values map via
//     StreamLatencyStore.MapRenderLevelToSpatialLayer.
//   - IsVisible defaults to true to keep pre-visibility-flag clients safe.
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record VideoLatencyReport(
    [property: DataMember, MemoryPackOrder(0)] double StreamOffsetMs,
    [property: DataMember, MemoryPackOrder(1)] double MedianDecodeTimeMs = -1,
    [property: DataMember, MemoryPackOrder(2)] int BufferDepth = -1,
    [property: DataMember, MemoryPackOrder(3)] double BufferSpanMs = -1,
    [property: DataMember, MemoryPackOrder(4)] VideoQualityLevel? RenderQuality = null,
    [property: DataMember, MemoryPackOrder(5)] bool IsVisible = true);
