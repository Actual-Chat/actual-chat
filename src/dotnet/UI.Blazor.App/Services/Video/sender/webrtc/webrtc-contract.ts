// Shared types for the WebRTC sender backend. Crosses the main↔worker
// boundary, so everything here must be structured-clone-safe (no closures,
// no MediaStream refs). The encoded frames themselves never cross via
// postMessage — they reach the worker through the Encoded Transform port
// (RTCRtpScriptTransform), set up on main with the recorder worker as target.

// Mirrors the .NET VideoFormat the receiver bootstraps its decoder from.
export interface WebRtcStreamFormat {
    codec: string;
    width: number;
    height: number;
    sourceWidth: number;
    sourceHeight: number;
    // Free-form codec hint (base64). Empty for H.264 (Annex-B in-band SPS/PPS).
    codecSettings: string;
}

// RPC payload: main → recorder worker, before any transform is attached.
// Configures streaming context + the wire sender the tap feeds.
export interface WebRtcStartOptions {
    chatId: string;
    apiUrl: string;
    // 0 = Camera, 1 = ScreenCast. Maps to .NET VideoSourceKind.
    sourceKind: number;
    serverClockOffsetMs?: number;
    // Top-tier format announced on the first keyframe (sender.init).
    format: WebRtcStreamFormat;
    layerCount: number;
    // Per-frame estimated duration in microseconds (1e6 / fps) — the tap has
    // no real frame-duration signal, so the producer supplies the nominal one.
    frameDurationMicros: number;
}

// Options object passed to `new RTCRtpScriptTransform(worker, options)` and
// read back in the worker as `transformer.options`. Identifies which ladder
// tier this transform's encoded frames belong to.
export interface WebRtcTierTransformOptions {
    kind: 'webrtc-tier';
    tier: number;
    layerId: number;
    layerCount: number;
    width: number;
    height: number;
    codec: string;
}

export function isWebRtcTierTransformOptions(o: unknown): o is WebRtcTierTransformOptions {
    return !!o && typeof o === 'object'
        && (o as { kind?: unknown }).kind === 'webrtc-tier';
}

// Receiver-side transform on the loopback's inbound PC. Frames are read and
// DISCARDED before the decoder — the loopback exists only to make the encoder
// run, so decoding the echoed stream is pure waste. RTP still flows (the
// transform is post-depacketize), so RTCP/transport-cc feedback keeps the
// sender's BWE healthy.
export interface WebRtcDropTransformOptions {
    kind: 'webrtc-drop';
    tier: number;
}

export function isWebRtcDropTransformOptions(o: unknown): o is WebRtcDropTransformOptions {
    return !!o && typeof o === 'object'
        && (o as { kind?: unknown }).kind === 'webrtc-drop';
}
