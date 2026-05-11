using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Video;

// End-to-end frame-drop attribution. Mirrors the TypeScript enum in
// src/dotnet/UI.Blazor.App/Services/Video/frame-drop-trace.ts. Numeric
// values are wire-stable: append new stages, never renumber. Byte-backed
// for wire compactness — the trace travels on every frame DTO.
[SuppressMessage("Design", "CA1028:Enum storage should be Int32",
    Justification = "byte is intentional — wire size matters; max stage count < 256.")]
public enum FrameDropStage : byte
{
    None = 0,
    SenderSource = 1,
    SenderFloodGate = 2,
    SenderStampCaptureTime = 3,
    SenderAttachSourceDims = 4,
    SenderDownscale = 5,
    SenderApplyKeyframePolicy = 6,
    SenderEncode = 7,
    SenderWireSend = 8,
    SenderPushPullBuffer = 9,
    SenderRpcStream = 10,

    ServerPushStream = 20,
    ServerProcessFrames = 21,
    ServerMemoizer = 22,
    ServerSkipWhile = 23,
    ServerReceiveQualityFilter = 24,
    ServerRpcStream = 25,

    ReceiverPull = 40,
    ReceiverEpochReset = 41,
    ReceiverEncodedBuffer = 42,
    ReceiverDecode = 43,
    ReceiverPresent = 44,
}
