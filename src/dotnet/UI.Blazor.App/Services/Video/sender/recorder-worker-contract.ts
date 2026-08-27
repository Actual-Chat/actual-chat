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
    // Diagnostics: raw encoder-fail-injection value ('<category>[:worker]') —
    // threaded from main because workers can't read localStorage.
    encoderFailInjection?: string;
    keyframeIntervalFrames: number;
    maxKeyFrameIntervalMs?: number;
    // Idle keepalive cadence: re-emit the last captured frame when the source
    // stalls for this long (static screencast content). <= 0/absent = disabled.
    keepAlivePeriodMs?: number;
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

// Per-stage tally of the preview tap, pulled to main because the inspector
// exposes no worker target — anything logged in this realm is unreadable.
// Counters are cumulative; timestamps are worker `performance.now()`, so only
// their deltas are meaningful on main.
export interface PreviewTrace {
    forwarded: number;
    noConsumer: number;
    refused: number;
    cloneFailed: number;
    writeCalled: number;
    writeResolved: number;
    writeRejected: number;
    reported: number;
    lastForwardedAtMs: number;
    lastWriteResolvedAtMs: number;
    lastDesiredSize: number | null;
    lastError: string;
}

// Structural subset of `AppConstants` — anything assignable to AppConstants fits.
export interface AppConstantsLike {
    readonly appName: string;
    readonly prodHost: string;
    readonly video: unknown;
    readonly audio: unknown;
}

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
    // Renegotiates the worker-owned capture clone's frame rate (Safari path).
    // The clone and main's original share one source, which captures at the max
    // of its consumers' rates — main must constrain its original in tandem.
    // False ⇒ no worker-owned track or the constraint was rejected.
    setCaptureFrameRate(fps: number): Promise<boolean>;
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
    getPreviewTrace(): Promise<PreviewTrace>;
    stop(): Promise<void>;

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
