namespace ActualChat.Video;

// Response to IStreamServer.ReportVideoLatency. Carries server-authoritative
// info about which spatial layer the SFU is currently forwarding to this peer
// and the coded WxH of the most recent frame on that layer. Used by the client
// diagnostics modal to show the actual delivered layer (vs sender's max).
//
// Sentinel values:
//   - ForwardedSpatialLayerId = -1 → no frame yet forwarded to this peer.
//   - ForwardedWidth/Height = 0 → unknown (no frame seen yet).
//   - ObservedMaxSpatialLayer = 0 → producer hasn't emitted above base.
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record VideoLatencyReportResponse(
    [property: DataMember, MemoryPackOrder(0)] int ForwardedSpatialLayerId = -1,
    [property: DataMember, MemoryPackOrder(1)] int ForwardedWidth = 0,
    [property: DataMember, MemoryPackOrder(2)] int ForwardedHeight = 0,
    [property: DataMember, MemoryPackOrder(3)] int ObservedMaxSpatialLayer = 0)
{
    public static readonly VideoLatencyReportResponse Empty = new();
}
