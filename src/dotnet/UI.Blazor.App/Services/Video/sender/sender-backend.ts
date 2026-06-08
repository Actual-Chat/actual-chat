// Common control surface for sender backends. The quality controller drives
// this interface regardless of how frames are actually encoded:
//   - WebCodecs: implemented by the worker `Recorder` (one VideoEncoder per
//     tier); the main-thread `VideoRecorder` already exposes equivalents
//     (reconfigureLayers / setTargetFps / requestKeyframe / setGateOpen).
//   - WebRTC: implemented by `WebRtcSender` (N single-encoding loopback PCs,
//     encoded frames tapped via RTCRtpScriptTransform).
//
// Keeping the surface identical lets the QC layer treat the two backends
// interchangeably (Phase 2 of the WebRTC-backend plan).

import type { EncoderConfigPerLayer } from '../operators/encode';

export type SenderBackendKind = 'webcodecs' | 'webrtc';

export interface ISenderBackend {
    readonly kind: SenderBackendKind;
    readonly isRunning: boolean;

    // Hot-apply the bottom-first ladder without tearing the wire stream down.
    // WebCodecs: LayerLadderController.setConfigs. WebRTC: per-PC setParameters
    // (maxBitrate / scaleResolutionDownBy / active), pinned to hold resolution.
    reconfigureLayers(configs: readonly EncoderConfigPerLayer[]): void | Promise<void>;

    // Demand-driven target fps. <=0 = idle (stop encoding, keep camera warm).
    setTargetFps(fps: number): void | Promise<void>;

    // Force the next frame of every active tier to be a keyframe.
    // WebCodecs: keyframe policy flag. WebRTC: encoding active-toggle (Chrome)
    // + RTCRtpScriptTransformer.generateKeyFrame (Safari/FF).
    requestKeyframe(): void | Promise<void>;

    // Open/close the wire gate without restarting the encoder.
    setGateOpen(open: boolean): void | Promise<void>;

    stop(): void | Promise<void>;
}
