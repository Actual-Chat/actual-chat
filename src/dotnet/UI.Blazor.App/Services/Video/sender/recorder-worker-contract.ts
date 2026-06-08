// RPC contract for the recorder worker. Pure types, no `actuallab-rpc`
// dependency — shareable between main and worker without dragging the
// transport in. Non-clonable collaborators (encoder/sender/downscaler
// factories) are built inside the worker, not passed across.

import type { RpcNoWait } from 'rpc';
import type { EncoderConfigPerLayer } from '../operators/encode';
import type { DownscalerMode } from '../operators/downscale';
import type { RecorderStats } from '../frame-envelopes';
import type { SharedSettingsWorker } from 'shared-settings-worker';
import type { VideoTraceKillPeriod } from '../frame-drop-trace';
import type { WebRtcStartOptions } from './webrtc/webrtc-contract';

// Must round-trip through `structuredClone` — no closures, no MediaStream refs.
export interface WireSafeRecorderConfig {
    chatId: string;
    apiUrl: string;
    // 0 = Camera, 1 = ScreenCast. Maps to .NET VideoSourceKind.
    sourceKind?: number;
    // Captured once at recorder start from track.getSettings().facingMode === 'user'.
    isFrontCamera?: boolean;
    isIos?: boolean;
    serverClockOffsetMs?: number;
    encoderConfigs: readonly EncoderConfigPerLayer[];
    // Fixed display ceiling `normalize` targets (full-ladder top), independent of
    // the active encode ladder, so the self-preview stays full-res when the active
    // ladder shrinks toward L0. Defaults to the active top when absent.
    normalizeSize?: { width: number; height: number };
    // Sender downscaler backend (diagnostics toggle). Defaults to 'webgl'.
    downscalerMode?: DownscalerMode;
    keyframeIntervalFrames: number;
    maxKeyFrameIntervalMs?: number;
    // Defaults to 'prefer-hardware'. Set to 'no-preference' as the 1-tier
    // last-resort fallback when 3-tier and 2-tier probes both fail — lets
    // the browser fall back to a SW encoder on machines where the HW
    // encoder slot is broken or exhausted (AMD iGPU + Windows MFT, etc.).
    hardwareAcceleration?: HardwareAcceleration;
    // When false the recorder pipeline starts with the wire-gate CLOSED:
    // encode + downstream operators run but no chunk reaches the server.
    // Caller flips it open via `setGateOpen(true)`. Defaults to true to
    // preserve legacy `startRecording → ship immediately` behavior.
    initialGateOpen?: boolean;
}

export interface RecorderWorkerOptions {
    sourceStartedAtMs: number;
    config: WireSafeRecorderConfig;
    // Set by main when it could not build a main-side generator (Safari, where
    // MediaStreamTrackGenerator / VideoTrackGenerator are worker-only). The
    // worker then creates the preview generator in its own realm and ships the
    // track back via `onPreviewTrackReady`. When false/omitted with no
    // `previewWritable`, the preview uses the canvas fallback.
    createPreviewInWorker?: boolean;
}

export interface PreviewFramePresentation {
    rotation: number;
}

// Structural subset of `AppConstants` — anything assignable to AppConstants fits.
export interface AppConstantsLike { readonly appName: string; readonly prodHost: string; readonly video: unknown; readonly audio: unknown }

// Methods ordered by lifecycle:
//   init → connectivity → source → run → query → stop → dispose.
export interface RecorderWorker extends SharedSettingsWorker {
    init(appConstants: AppConstantsLike): Promise<void>;
    onConnectivityUpdate(isOnline: boolean, isConnected: boolean, isBlazorServer: boolean): Promise<void>;

    // Primary Chromium capture path: main builds the MediaStreamTrackProcessor
    // and transfers its `readable` here; the worker pulls frames source-bound,
    // with no main-thread rVFC tick. `pushFrame`/`endSource` below is the rVFC
    // fallback for browsers/cases where MSTP is unavailable.
    setSource(readable: ReadableStream<VideoFrame>): Promise<void>;
    // Safari capture path: MSTP is worker-only there, so main transfers a CLONE
    // of the camera track and the worker builds the MediaStreamTrackProcessor in
    // its own realm. Returns true when MSTP was built (worker owns the track and
    // stops it on run end); false ⇒ no worker MSTP, main falls back to rVFC and
    // the (already-stopped) clone is discarded.
    setSourceTrack(track: MediaStreamTrack): Promise<boolean>;
    // Callers MUST keep at most one push in flight; otherwise transferred
    // VideoFrames pile up in the message queue ahead of the slot that closes them.
    pushFrame(frame: VideoFrame, noWait?: RpcNoWait): Promise<void>;
    endSource(noWait?: RpcNoWait): Promise<void>;

    // Trailing transferable arg: main constructs the preview
    // MediaStreamTrackGenerator, attaches its track locally, and transfers
    // only the writable into the worker.
    start(
        opts: RecorderWorkerOptions,
        previewWritable?: WritableStream<VideoFrame>,
    ): Promise<void>;
    setTraceKill(avgPeriod: VideoTraceKillPeriod, stage: number): Promise<boolean>;
    requestKeyframe(): Promise<void>;
    // Open/close the wire-gate on a running pipeline. Independent of
    // start/stop so warmup → live transitions don't restart the encoder.
    setGateOpen(open: boolean): Promise<void>;
    // Hot-apply: swap the running pipeline's encoder ladder without
    // recreating the wire RpcStream. Caller must ensure codec parity with
    // the active run (codec swap still requires stop+start).
    reconfigureLayers(configs: readonly EncoderConfigPerLayer[]): Promise<void>;
    // Demand-driven target fps for temporal pacing. <=0 drops every frame
    // (idle: stop encoding, keep camera warm). Hot-applied, no restart.
    setTargetFps(fps: number, noWait?: RpcNoWait): Promise<void>;
    getStats(): Promise<RecorderStats>;
    stop(): Promise<void>;

    // ---- WebRTC sender backend (experimental, parallel to the above) ----
    // The encoded frames arrive via the Encoded Transform port, not RPC: main
    // attaches `RTCRtpScriptTransform(thisWorker, tierOptions)` to each tier's
    // RTCRtpSender. `webRtcStart` configures the wire sender the tap feeds;
    // `webRtcGenerateKeyFrame` re-keys one tier (Safari path).
    webRtcStart(opts: WebRtcStartOptions): Promise<void>;
    webRtcStop(): Promise<void>;
    webRtcGenerateKeyFrame(tier: number): Promise<void>;
    // Cumulative top-tier frames pushed to the wire (RpcStream) — the real
    // send rate is the per-second delta. Top tier encodes every source moment,
    // so its count is the source/send fps without dividing by layer count.
    webRtcGetSentFrameCount(): Promise<number>;

    // No-op today — the new pipeline lazy-creates the peer per stream and the
    // reconnect loop lives in StreamingApi.
    disconnectApi(): Promise<void>;
}

// Callbacks ordered by lifecycle: start → end → error (error is anytime).
export interface RecorderWorkerCallbacks {
    // `codecSettings` carries the codec descriptor the receiver needs to bootstrap its decoder.
    onStreamCreated(codecSettings: string): void;
    onStreamEnded(reason: string): void;
    onError(error: string): void;
    onTraceKillInjected(): void;
    onPreviewFrame(frame: VideoFrame): void | Promise<void>;
    onPreviewFramePresentation(presentation: PreviewFramePresentation): void;
    // Worker-created preview track (Safari path). Non-null ⇒ main attaches it
    // to the preview <video srcObject>; null ⇒ worker has no generator, main
    // uses the canvas fallback. Mirrors the player's `onTrackReady`.
    onPreviewTrackReady(track: MediaStreamTrack | null): void;
}
