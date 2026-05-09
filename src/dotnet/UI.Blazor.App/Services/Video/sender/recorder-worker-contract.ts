// RPC contract for the recorder worker. Pure types, no `actuallab-rpc`
// dependency — shareable between main and worker without dragging the
// transport in. Non-clonable collaborators (encoder/sender/downscaler
// factories) are built inside the worker, not passed across.

import type { RpcNoWait } from 'rpc';
import type { EncoderConfigPerLayer } from '../operators/encode';
import type { VideoRecordingStats } from '../frame-envelopes';
import type { SharedSettingsWorker } from 'shared-settings-worker';

// Must round-trip through `structuredClone` — no closures, no MediaStream refs.
export interface WireSafeRecorderConfig {
    chatId: string;
    apiUrl: string;
    // 0 = Camera, 1 = ScreenCast. Maps to .NET VideoSourceKind.
    sourceKind?: number;
    serverClockOffsetMs?: number;
    encoderConfigs: readonly EncoderConfigPerLayer[];
    keyframeIntervalFrames: number;
    maxKeyFrameIntervalMs?: number;
}

export interface RecorderWorkerOptions {
    sourceStartedAtMs: number;
    config: WireSafeRecorderConfig;
}

// Structural subset of `AppConstants` — anything assignable to AppConstants fits.
export interface AppConstantsLike { readonly appName: string; readonly prodHost: string; readonly video: unknown; readonly audio: unknown }

export interface RecorderWorker extends SharedSettingsWorker {
    init(appConstants: AppConstantsLike): Promise<void>;
    // Test path. Production prefers `pushFrame`+`endSource` because Chrome's
    // MSTP-readable cross-realm bridge starves after a few frames.
    setSource(readable: ReadableStream<VideoFrame>): Promise<void>;
    // Callers MUST keep at most one push in flight; otherwise transferred
    // VideoFrames pile up in the message queue ahead of the slot that closes them.
    pushFrame(frame: VideoFrame, noWait?: RpcNoWait): Promise<void>;
    endSource(noWait?: RpcNoWait): Promise<void>;
    start(opts: RecorderWorkerOptions): Promise<void>;
    stop(): Promise<void>;
    requestKeyframe(): Promise<void>;
    getStats(): Promise<VideoRecordingStats>;
    onConnectivityUpdate(isOnline: boolean, isConnected: boolean, isBlazorServer: boolean): Promise<void>;
    // No-op today — the new pipeline lazy-creates the peer per stream and the
    // reconnect loop lives in StreamingApi.
    disconnectApi(): Promise<void>;
}

export interface RecorderWorkerCallbacks {
    onError(error: string): void;
    // `codecSettings` carries the codec descriptor the receiver needs to bootstrap its decoder.
    onStreamCreated(codecSettings: string): void;
    onStreamEnded(reason: string): void;
}
