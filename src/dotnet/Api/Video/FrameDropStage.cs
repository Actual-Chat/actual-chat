namespace ActualChat.Video;

// End-to-end frame-drop attribution. Mirrors the TypeScript enum in
// src/dotnet/UI.Blazor.App/Services/Video/frame-drop-trace.ts. Byte-backed
// for wire compactness — the trace travels on every frame DTO.
// Ranges: 1-30 sender, 31-60 server, 61-90 receiver.
[SuppressMessage("Design", "CA1028:Enum storage should be Int32",
    Justification = "byte is intentional — wire size matters; max stage count < 256.")]
public enum FrameDropStage : byte
{
    None = 0,

    SenderSource = 1,
    SenderFloodGate = 2,
    SenderDownscale = 3,
    SenderEncode = 4,

    ServerPushStream = 31,
    ServerMemoizer = 32,
    ServerSkipWhile = 33,
    ServerReceiveQualityFilter = 34,

    ReceiverPull = 61,
    ReceiverEncodedBuffer = 62,
    ReceiverDecode = 63,
    ReceiverPresent = 64,
    ReceiverSkipToLive = 65,
}
