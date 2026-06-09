// WebRTC sender backend — main-thread controller. Owns the camera track and
// N single-encoding loopback PeerConnections (one per ladder tier). WebRTC
// encodes (zero-copy HW path), and each tier's encoded frames are tapped via
// RTCRtpScriptTransform whose target is a recorder-worker instance — the worker
// maps them to the existing wire DTO and pushes over the same RpcStream the
// WebCodecs path uses. The server, fan-out and receiver are unchanged.
//
// This is the experimental, flag-free entry point: `globalThis.voxtWebRtc`.
// It deliberately does NOT touch the production `VideoRecorder`; it reuses the
// real PushStream + signaling under a chatId so a second browser viewing that
// chat sees the WebRTC-encoded video through the unchanged receiver UI.

import { getLogs } from 'logging';
import { AC } from 'app-constants';
import { DeviceInfo } from 'device-info';
import { Versioning } from 'versioning';
import { rpcClientServer } from 'rpc';
import { SharedSettings } from 'shared-settings';
import { SharedSettingsWorkerSync } from 'shared-settings-worker';
import type { Disposable } from 'disposable';
import { MediaCapture } from '../../services/media-capture';
import type { EncoderConfigPerLayer } from '../../operators/encode';
import {
    pickWebRtcCodec,
    codecsForCategory,
    type WebRtcCodecCategory,
} from '../../webrtc-codec-support';
import type {
    RecorderWorker,
    RecorderWorkerCallbacks,
} from '../recorder-worker-contract';
import type { ISenderBackend } from '../sender-backend';
import { LoopbackTier } from './loopback-connection';
import type { WebRtcStartOptions, WebRtcTierTransformOptions } from './webrtc-contract';

const { infoLog, warnLog, errorLog } = getLogs('VideoRecorder');

// Unconditional console output — this is a console-driven debug harness, so
// verification must not depend on per-scope log levels being enabled.
const C = (...a: unknown[]): void => console.info('[voxtWebRtc]', ...a);
const CE = (...a: unknown[]): void => console.error('[voxtWebRtc]', ...a);

const KEYFRAME_INTERVAL_MS = 3000;
const FPS = 30;

interface TierSpec {
    width: number;
    height: number;
    maxBitrate: number;
}

// Bottom-first ladder. Mobile = 2 tiers; desktop = 3 (1280×720 top).
function buildTierSpecs(): TierSpec[] {
    const desktop: TierSpec[] = [
        { width: 320, height: 184, maxBitrate: 250_000 },
        { width: 640, height: 360, maxBitrate: 700_000 },
        { width: 1280, height: 720, maxBitrate: 1_800_000 },
    ];
    return DeviceInfo.isMobile ? desktop.slice(0, 2) : desktop;
}

// Per simulcast layer (= per tier) diagnostics, sampled from getStats().
export interface WebRtcTierDiagnostics {
    layerId: number;
    width: number;
    height: number;
    fps: number;
    framesEncoded: number;
    keyFramesEncoded: number;
    bitrateKbps: number;
    targetBitrateKbps: number;
    encoderImplementation: string;
    hardwareAccelerated: boolean;
    active: boolean;
}

export interface WebRtcDiagnostics {
    codec: string;
    sourceWidth: number;
    sourceHeight: number;
    layerCount: number;
    tiers: WebRtcTierDiagnostics[];
    aggregateBitrateKbps: number;
    encoderImplementation: string;
    hardwareAccelerated: boolean;
}

// HW unless the encoder name matches a known software implementation. Used only
// when getStats() doesn't expose powerEfficientEncoder.
function isSoftwareEncoder(impl: string): boolean {
    return /libvpx|libaom|openh264|\bsvt\b|software|fallback/i.test(impl);
}

export interface WebRtcStartParams {
    chatId: string;
    deviceId?: string;
    apiUrl?: string;
    preferredCodecs?: WebRtcCodecCategory[];
    // Pre-acquired camera track (e.g. from the recorder's warmup preview). When
    // provided, the sender reuses it and does NOT stop it on stop() — the caller
    // owns its lifetime. When omitted, the sender opens (and stops) its own.
    track?: MediaStreamTrack;
}

export class WebRtcSender implements ISenderBackend {
    readonly kind = 'webrtc' as const;

    private workerInstance: Worker | null = null;
    private worker: (RecorderWorker & Disposable) | null = null;
    private sharedSettingsReg: Disposable | null = null;
    private track: MediaStreamTrack | null = null;
    private tiers: LoopbackTier[] = [];
    private specs: TierSpec[] = [];
    private cameraWidth = 0;
    private cameraHeight = 0;
    private keyframeTimer: ReturnType<typeof setInterval> | null = null;
    private running = false;
    // false when the camera track was supplied by the caller (recorder warmup),
    // so stop() must not stop a track it doesn't own.
    private ownsTrack = true;

    // Measured send rate (top-tier frames pushed to the wire / sec), sampled
    // from the worker each second. This is the real transport throughput, not a
    // nominal camera rate.
    private fpsTimer: ReturnType<typeof setInterval> | null = null;
    private lastSentCount = 0;
    private lastSentAtMs = 0;
    private measuredFps = 0;

    // Diagnostics: chosen codec + sampled per-tier (simulcast layer) stats.
    private codecString = '';
    private lastDiag: WebRtcDiagnostics | null = null;
    private lastBytesByTier = new Map<number, { bytes: number; ts: number }>();

    get isRunning(): boolean { return this.running; }

    getPreviewTrack(): MediaStreamTrack | null { return this.track; }

    // Cached per-layer encoder diagnostics (resolution, fps, bitrate, frames,
    // HW/SW per tier). Updated once/sec while running. Null until first sample.
    getDiagnostics(): WebRtcDiagnostics | null { return this.lastDiag; }

    // Top-tier frames pushed to the wire per second (measured). 0 until the
    // first two samples land.
    getMeasuredFps(): number { return this.measuredFps; }

    async start(params: WebRtcStartParams): Promise<void> {
        if (this.running) {
            warnLog?.log('start: already running — stop() first');
            return;
        }
        const apiUrl = params.apiUrl ?? defaultApiUrl();
        C(`start() called — chatId=${params.chatId} apiUrl=${apiUrl}`);
        try {
            await this.startImpl(params, apiUrl);
        } catch (e) {
            CE('start() FAILED:', e);
            await this.stop().catch(() => { /* ignore */ });
            throw e;
        }
    }

    private async startImpl(params: WebRtcStartParams, apiUrl: string): Promise<void> {
        // 1) Spawn a recorder-worker instance to host the transform + wire sender.
        const workerInstance = new Worker(Versioning.mapPath('/dist/videoRecorderWorker.js'), { type: 'module' });
        workerInstance.onerror = e => CE('recorder worker error:', e);
        this.workerInstance = workerInstance;
        const worker = rpcClientServer<RecorderWorker>('VideoRecorder.worker', workerInstance, makeCallbacks());
        this.worker = worker;
        C('worker spawned — calling init…');
        await worker.init(AC);
        // Bridge SharedSettings (session token the worker's RPC peer needs).
        SharedSettings.update({ apiUrl });
        this.sharedSettingsReg = SharedSettingsWorkerSync.register(worker);
        // Satisfy requireConnection so PushStream's AwaitForConnection resolves.
        await worker.onConnectivityUpdate(true, true, false);
        C('worker initialized + connectivity pushed');

        // 2) Pick codec (H.264 default — Annex-B, broad support, matches receiver).
        const codec = pickWebRtcCodec(params.preferredCodecs);
        if (!codec)
            throw new Error('WebRtcSender: no usable WebRTC send codec');
        this.codecString = codec.wireCodec;
        C(`codec picked: ${codec.category} → ${codec.wireCodec}`);

        // 3) Acquire camera at the top-tier resolution (or reuse the caller's).
        this.specs = buildTierSpecs();
        const top = this.specs[this.specs.length - 1];
        let track: MediaStreamTrack;
        if (params.track) {
            track = params.track;
            this.ownsTrack = false;
            C('reusing caller-supplied camera track');
        } else {
            track = await MediaCapture.captureCameraStream({
                deviceId: params.deviceId,
                frameRate: FPS,
                width: top.width,
                height: top.height,
            });
            this.ownsTrack = true;
        }
        this.track = track;
        const s = track.getSettings();
        this.cameraWidth = s.width ?? top.width;
        this.cameraHeight = s.height ?? top.height;
        // Only own the ended→stop wiring when we opened the track; otherwise the
        // caller (recorder warmup) owns the track's onended.
        if (this.ownsTrack)
            track.onended = () => { void this.stop(); };
        C(`camera acquired ${this.cameraWidth}x${this.cameraHeight}@${s.frameRate ?? FPS}, tiers=${this.specs.length}`);

        // 4) Configure the worker's wire sender.
        const startOpts: WebRtcStartOptions = {
            chatId: params.chatId,
            apiUrl,
            sourceKind: 0,
            format: {
                codec: codec.wireCodec,
                width: top.width,
                height: top.height,
                sourceWidth: this.cameraWidth,
                sourceHeight: this.cameraHeight,
                codecSettings: '',
            },
            layerCount: this.specs.length,
            frameDurationMicros: Math.round(1_000_000 / FPS),
        };
        C('calling worker.webRtcStart…');
        await worker.webRtcStart(startOpts);
        C('worker.webRtcStart done — creating loopback PCs');

        // 5) One loopback PC per tier, each tapped into the worker.
        const codecPrefs = codecsForCategory(codec.category);
        this.tiers = this.specs.map((spec, i) => {
            const transformOptions: WebRtcTierTransformOptions = {
                kind: 'webrtc-tier',
                tier: i,
                layerId: i,
                layerCount: this.specs.length,
                width: spec.width,
                height: spec.height,
                codec: codec.wireCodec,
            };
            return new LoopbackTier({
                track,
                worker: workerInstance,
                transformOptions,
                scaleResolutionDownBy: Math.max(1, this.cameraWidth / spec.width),
                maxBitrate: spec.maxBitrate,
                maxFramerate: FPS,
                codecPreferences: codecPrefs,
            });
        });
        C(`connecting ${this.tiers.length} loopback PC(s)…`);
        await Promise.all(this.tiers.map(t => t.connect().catch((e: unknown) => {
            CE(`tier ${t.tier} connect failed`, e);
        })));
        C('all tiers connected');

        this.running = true;

        // 6) Force an initial keyframe (so sender.init fires), then keep a
        // periodic keyframe cadence (no receiver→sender PLI path in v1).
        await this.requestKeyframe();
        this.keyframeTimer = setInterval(() => { void this.requestKeyframe(); }, KEYFRAME_INTERVAL_MS);
        this.fpsTimer = setInterval(() => { void this.sampleFps(); void this.sampleDiag(); }, 1000);
        C('LIVE — WebRTC sender running. Check chrome://webrtc-internals for the PCs.');
    }

    private async sampleDiag(): Promise<void> {
        const stats = await Promise.all(this.tiers.map(t => t.getStats().catch(() => null)));
        const now = performance.now();
        const tiers: WebRtcTierDiagnostics[] = [];
        let aggKbps = 0;
        for (const s of stats) {
            if (!s) continue;
            const prev = this.lastBytesByTier.get(s.tier);
            let kbps = 0;
            if (prev) {
                const dt = (now - prev.ts) / 1000;
                if (dt > 0) kbps = Math.max(0, (s.bytesSent - prev.bytes) * 8 / 1000 / dt);
            }
            this.lastBytesByTier.set(s.tier, { bytes: s.bytesSent, ts: now });
            const hw = s.powerEfficient ?? !isSoftwareEncoder(s.encoderImplementation);
            tiers.push({
                layerId: s.tier,
                width: s.width,
                height: s.height,
                fps: Math.round(s.fps),
                framesEncoded: s.framesEncoded,
                keyFramesEncoded: s.keyFramesEncoded,
                bitrateKbps: Math.round(kbps),
                targetBitrateKbps: Math.round(s.targetBitrate / 1000),
                encoderImplementation: s.encoderImplementation,
                hardwareAccelerated: hw,
                active: s.active,
            });
            aggKbps += kbps;
        }
        tiers.sort((a, b) => a.layerId - b.layerId);
        const top = tiers.length > 0 ? tiers[tiers.length - 1] : null;
        this.lastDiag = {
            codec: this.codecString,
            sourceWidth: this.cameraWidth,
            sourceHeight: this.cameraHeight,
            layerCount: tiers.length,
            tiers,
            aggregateBitrateKbps: Math.round(aggKbps),
            encoderImplementation: top?.encoderImplementation ?? '',
            hardwareAccelerated: tiers.length > 0 && tiers.every(t => t.hardwareAccelerated),
        };
    }

    private async sampleFps(): Promise<void> {
        const worker = this.worker;
        if (!worker) return;
        let count: number;
        try { count = await worker.webRtcGetSentFrameCount(); }
        catch { return; }
        const now = performance.now();
        if (this.lastSentAtMs > 0) {
            const dt = (now - this.lastSentAtMs) / 1000;
            if (dt > 0) this.measuredFps = Math.max(0, (count - this.lastSentCount) / dt);
        }
        this.lastSentCount = count;
        this.lastSentAtMs = now;
    }

    // Console-friendly snapshot: `voxtWebRtc.sender.status()`.
    status(): unknown {
        return {
            running: this.running,
            camera: `${this.cameraWidth}x${this.cameraHeight}`,
            tierCount: this.tiers.length,
            tiers: this.tiers.map(t => t.getState()),
        };
    }

    // ---- ISenderBackend ----

    reconfigureLayers(configs: readonly EncoderConfigPerLayer[]): void {
        const activeCount = configs.length;
        this.tiers.forEach((tier, i) => {
            if (i < activeCount) {
                const cfg = configs[i];
                tier.applyParameters({
                    active: true,
                    maxBitrate: cfg.bitrate,
                    scaleResolutionDownBy: Math.max(1, this.cameraWidth / cfg.width),
                    maxFramerate: cfg.framerate,
                });
            } else {
                tier.applyParameters({ active: false });
            }
        });
    }

    setTargetFps(fps: number): void {
        const active = fps > 0;
        for (const tier of this.tiers)
            tier.applyParameters({ active, ...(active ? { maxFramerate: fps } : {}) });
    }

    async requestKeyframe(): Promise<void> {
        await Promise.all(this.tiers.map(async tier => {
            await tier.forceKeyFrame();
            await this.worker?.webRtcGenerateKeyFrame(tier.tier);
        }));
    }

    setGateOpen(open: boolean): void {
        for (const tier of this.tiers)
            tier.applyParameters({ active: open });
    }

    async stop(): Promise<void> {
        if (!this.running && !this.workerInstance) return;
        this.running = false;
        if (this.keyframeTimer) { clearInterval(this.keyframeTimer); this.keyframeTimer = null; }
        if (this.fpsTimer) { clearInterval(this.fpsTimer); this.fpsTimer = null; }
        this.measuredFps = 0;
        this.lastSentCount = 0;
        this.lastSentAtMs = 0;
        this.lastDiag = null;
        this.lastBytesByTier.clear();
        for (const tier of this.tiers) tier.close();
        this.tiers = [];
        try { await this.worker?.webRtcStop(); } catch { /* ignore */ }
        try { this.sharedSettingsReg?.dispose(); } catch { /* ignore */ }
        this.sharedSettingsReg = null;
        try { this.worker?.dispose(); } catch { /* ignore */ }
        this.worker = null;
        try { this.workerInstance?.terminate(); } catch { /* ignore */ }
        this.workerInstance = null;
        // Only stop the track if we opened it; caller-supplied tracks are theirs.
        if (this.ownsTrack) { try { this.track?.stop(); } catch { /* ignore */ } }
        this.track = null;
        C('stop: done — all PCs closed, worker terminated');
    }
}

function defaultApiUrl(): string {
    const wsOrigin = location.origin.replace(/^http/, 'ws');
    return `${wsOrigin}/rpc/ws`;
}

function makeCallbacks(): RecorderWorkerCallbacks {
    return {
        onStreamCreated: () => { /* noop */ },
        onStreamEnded: (reason: string) => infoLog?.log(`stream ended: ${reason}`),
        onError: (error: string) => errorLog?.log(`worker error: ${error}`),
        onTraceKillInjected: () => { /* noop */ },
        onPreviewFrame: () => { /* noop */ },
        onPreviewFramePresentation: () => { /* noop */ },
        onPreviewTrackReady: () => { /* noop */ },
    };
}

// Debug entry point — start/stop the WebRTC sender from the console:
//   await voxtWebRtc.start('the-actual-one')
//   await voxtWebRtc.stop()
const singleton = new WebRtcSender();
(globalThis as unknown as { voxtWebRtc?: unknown }).voxtWebRtc = {
    start: (chatId: string, deviceId?: string, apiUrl?: string) =>
        singleton.start({ chatId, deviceId, apiUrl }),
    stop: () => singleton.stop(),
    sender: singleton,
};
