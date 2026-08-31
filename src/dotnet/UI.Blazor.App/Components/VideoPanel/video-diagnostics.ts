import { getActiveRecorder, getAllActiveRecorders, type OwnStreamDiagnostics } from './video-recorder';
import { getActivePlayers, recordRequestedReceiveQuality, type RemoteStreamDiagnostics } from './video-player';
import {
    detectSupportedDecoderCodecs as detectSupportedDecoderCodecsImpl,
    getForceDecodeCodec as getForceDecodeCodecImpl,
    getPreferredEncodeCodec as getPreferredEncodeCodecImpl,
    setForceDecodeCodec as setForceDecodeCodecImpl,
    setPreferredEncodeCodec as setPreferredEncodeCodecImpl,
    type CodecCategory,
} from '../../Services/Video/codec-support';
import {
    getDownscalerMode as getDownscalerModeImpl,
    setDownscalerMode as setDownscalerModeImpl,
} from '../../Services/Video/downscaler-mode';
import type { DownscalerMode } from '../../Services/Video/operators/downscale';
import {
    getCaptureFpsOverride as getCaptureFpsOverrideImpl,
    setCaptureFpsOverride as setCaptureFpsOverrideImpl,
} from '../../Services/Video/capture-fps-override';
import { ServerClock } from 'clocks';
import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoPipeline');

export { collectPresentRate } from './present-rate-meter';

export interface OwnStreamDiagnosticsSnapshot {
    stream: OwnStreamDiagnostics | null;
}

export function collectOwnStreamDiagnostics(kind: number): OwnStreamDiagnosticsSnapshot {
    const recorder = getActiveRecorder(kind);
    return {
        stream: recorder?.getDiagnostics() ?? null,
    };
}

// Main-thread JS ServerClock offset — the value the video A/V-sync math actually
// reads, distinct from the C# ServerTimeSync source that feeds it.
export function collectServerClockDiagnostics(): { offsetMs: number } {
    return { offsetMs: ServerClock.offsetMs };
}

export async function collectRemoteStreamDiagnostics(streamId: string): Promise<RemoteStreamDiagnostics | null> {
    const player = getActivePlayers().get(streamId);
    if (!player) return null;
    return player.getDiagnosticsAsync();
}

export async function collectActiveStreamHints(): Promise<{ streamId: string; currentLayerId: number }[]> {
    const result: { streamId: string; currentLayerId: number }[] = [];
    for (const [streamId, player] of getActivePlayers()) {
        try {
            const d = await player.getDiagnosticsAsync();
            const layer = d.forwarded?.ForwardedLayerId ?? 0;
            result.push({ streamId, currentLayerId: layer });
        } catch {
            result.push({ streamId, currentLayerId: 0 });
        }
    }
    return result;
}

// Invoked from VideoQualityUI on the background→foreground transition.
export function restartActivePlayersForResume(): number {
    let restarted = 0;
    for (const player of getActivePlayers().values())
        if (player.restartForResume())
            restarted++;
    return restarted;
}

export function setRequestedReceiveQuality(
    streamId: string,
    layerId: number | null
): void {
    if (layerId === null) {
        recordRequestedReceiveQuality(streamId, null);
        return;
    }

    recordRequestedReceiveQuality(streamId, { layerId });
}

// Diagnostic settings — toggleable from VideoDiagnosticsSettingsModal.
// Backed by localStorage; codec flags take effect on the next codec
// detection pass (typically the next stream).
export interface VideoDebugSettings {
    forceDecodeCodec: string | null;
    preferredEncodeCodec: string | null;
    maxOutboundLayerCount: number | null;
    maxInboundLayerCount: number | null;
    estBandwidthMultiplier: number;
    downscalerMode: DownscalerMode;
    captureFpsOverride: number | null;
}

export function getVideoDebugSettings(): VideoDebugSettings {
    return {
        forceDecodeCodec: getForceDecodeCodecImpl(),
        preferredEncodeCodec: getPreferredEncodeCodecImpl(),
        maxOutboundLayerCount: getLayerCount(OUTBOUND_LAYER_COUNT_KEY),
        maxInboundLayerCount: getLayerCount(INBOUND_LAYER_COUNT_KEY),
        estBandwidthMultiplier: getBandwidthMultiplier(),
        downscalerMode: getDownscalerModeImpl(),
        captureFpsOverride: getCaptureFpsOverrideImpl(),
    };
}

export function setVideoDebugCaptureFpsOverride(fps: number | null): void {
    setCaptureFpsOverrideImpl(fps);
    for (const recorder of getAllActiveRecorders())
        recorder.refreshCaptureFps();
}

// The categories this client can decode, for RegisterMember. Honours the
// "Force decode codec" override.
export function getSupportedDecoderCodecs(): Promise<string[]> {
    return detectSupportedDecoderCodecsImpl();
}

export function setVideoDebugForceDecodeCodec(codec: string | null): void {
    setForceDecodeCodecImpl(codec ? (codec as CodecCategory) : null);
    void applyCodecOverrides(true);
}

export function setVideoDebugPreferredEncodeCodec(codec: string | null): void {
    setPreferredEncodeCodecImpl(codec ? (codec as CodecCategory) : null);
    void applyCodecOverrides(false);
}

// Set by ChatVideoUI once its registration loop starts.
let memberRegistrationHook: DotNet.DotNetObject | null = null;

export function initVideoMemberRegistration(hook: DotNet.DotNetObject): void {
    memberRegistrationHook = hook;
}

// Re-runs selection on every live recorder so an override takes effect on the
// current stream, and — when what we ADVERTISE changed — asks the server to
// re-read this client's decode set instead of waiting for the next heartbeat.
async function applyCodecOverrides(decodeSetChanged: boolean): Promise<void> {
    // Told through ChatVideoUI, not through a recorder: the client forcing a
    // decode codec is usually a viewer with its camera off, and it has no
    // recorder to carry the message. Without this the override waited for the
    // next registration heartbeat.
    if (decodeSetChanged && memberRegistrationHook) {
        try {
            await memberRegistrationHook.invokeMethodAsync('RequestMemberReregistration');
        }
        catch (e) {
            warnLog?.log(`applyCodecOverrides: re-registration request failed: ${String(e)}`);
        }
    }
    for (const recorder of getAllActiveRecorders()) {
        try {
            await recorder.refreshCodecSelection();
        }
        catch (e) {
            warnLog?.log(`applyCodecOverrides failed: ${String(e)}`);
        }
    }
}

export interface VideoCodecState {
    forceDecodeCodec: string | null;
    preferredEncodeCodec: string | null;
    advertisedDecoderCodecs: string[];
    senders: { kind: number; codec: string | null; bundlesPerSec: number }[];
    receivers: { streamId: string; codec: string | null; presentedPerSec: number }[];
}

export async function collectVideoCodecState(): Promise<VideoCodecState> {
    return {
        forceDecodeCodec: getForceDecodeCodecImpl(),
        preferredEncodeCodec: getPreferredEncodeCodecImpl(),
        advertisedDecoderCodecs: await detectSupportedDecoderCodecsImpl(),
        senders: getAllActiveRecorders().map(r => ({
            kind: r.peekKind(),
            codec: r.peekCodec(),
            bundlesPerSec: Math.round(r.peekBundlesPerSec()),
        })),
        receivers: [...getActivePlayers().entries()].map(([streamId, p]) => ({
            streamId,
            codec: p.peekCodec(),
            presentedPerSec: Math.round(p.peekPresentedPerSec()),
        })),
    };
}

// Console surface: `debugUI.video.*`. Everything the diagnostics modal can do,
// callable without clicking through it — which is how these actually get
// exercised while debugging a live call.
export function initVideoDebugConsole(): void {
    const api = {
        state: collectVideoCodecState,
        settings: getVideoDebugSettings,
        setForceDecodeCodec: setVideoDebugForceDecodeCodec,
        setPreferredEncodeCodec: setVideoDebugPreferredEncodeCodec,
        restart: () => applyCodecOverrides(true),
    };
    const root = globalThis as unknown as { debugUI?: Record<string, unknown> };
    if (root.debugUI) {
        root.debugUI.video = api;
        return;
    }

    // DebugUI.init() assigns globalThis.debugUI from C#, which happens after
    // whenBlazorReady resolves. Intercepting the assignment beats racing it
    // with a timer.
    let current: Record<string, unknown> | undefined;
    Object.defineProperty(root, 'debugUI', {
        configurable: true,
        get: () => current,
        set: (value: Record<string, unknown>) => {
            current = value;
            value.video = api;
        },
    });
}

export function setVideoDebugDownscalerMode(mode: DownscalerMode): void {
    setDownscalerModeImpl(mode);
}

export function setVideoDebugMaxOutboundLayerCount(layerCount: number | null): void {
    setLayerCount(OUTBOUND_LAYER_COUNT_KEY, layerCount);
}

export function setVideoDebugMaxInboundLayerCount(layerCount: number | null): void {
    setLayerCount(INBOUND_LAYER_COUNT_KEY, layerCount);
}

export function setVideoDebugEstBandwidthMultiplier(value: number): void {
    try {
        if (!Number.isFinite(value) || value === 1) {
            globalThis.localStorage.removeItem(EST_BANDWIDTH_MULTIPLIER_KEY);
            return;
        }

        globalThis.localStorage.setItem(EST_BANDWIDTH_MULTIPLIER_KEY, String(value));
    } catch {
        // Debug-only setting; ignore storage failures in private/sandboxed contexts.
    }
}

const OUTBOUND_LAYER_COUNT_KEY = 'video.debug.maxOutboundLayerCount';
const INBOUND_LAYER_COUNT_KEY = 'video.debug.maxInboundLayerCount';
const EST_BANDWIDTH_MULTIPLIER_KEY = 'video.debug.estBandwidthMultiplier';

function getLayerCount(key: string): number | null {
    try {
        const raw = globalThis.localStorage.getItem(key);
        if (raw === null)
            return null;

        return normalizeLayerCount(Number(raw));
    } catch {
        return null;
    }
}

function setLayerCount(key: string, layerCount: number | null): void {
    try {
        const normalized = normalizeLayerCount(layerCount);
        if (normalized === null) {
            globalThis.localStorage.removeItem(key);
            return;
        }

        globalThis.localStorage.setItem(key, String(normalized));
    } catch {
        // Debug-only setting; ignore storage failures in private/sandboxed contexts.
    }
}

function normalizeLayerCount(layerCount: number | null): number | null {
    if (typeof layerCount !== 'number' || !Number.isInteger(layerCount))
        return null;
    if (layerCount < 1 || layerCount > 3)
        return null;
    return layerCount;
}

function getBandwidthMultiplier(): number {
    try {
        const raw = globalThis.localStorage.getItem(EST_BANDWIDTH_MULTIPLIER_KEY);
        if (raw === null) return 1;
        const v = Number(raw);
        return Number.isFinite(v) && v > 0 ? v : 1;
    } catch {
        return 1;
    }
}
