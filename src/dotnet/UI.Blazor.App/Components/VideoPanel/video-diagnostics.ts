import { getActiveRecorder, type OwnStreamDiagnostics } from './video-recorder';
import { getActivePlayers, recordRequestedReceiveQuality, type RemoteStreamDiagnostics } from './video-player';
import {
    getForceH264Only as getForceH264OnlyImpl,
    setForceH264Only as setForceH264OnlyImpl,
} from '../../Services/Video/codec-support';
import {
    getDownscalerMode as getDownscalerModeImpl,
    setDownscalerMode as setDownscalerModeImpl,
} from '../../Services/Video/downscaler-mode';
import {
    getSenderBackendMode as getSenderBackendModeImpl,
    setSenderBackendMode as setSenderBackendModeImpl,
} from '../../Services/Video/sender-backend-mode';
import type { DownscalerMode } from '../../Services/Video/operators/downscale';
import { ServerClock } from 'clocks';
import type { SenderBackendKind } from '../../Services/Video/sender/sender-backend';

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
    forceH264Only: boolean;
    maxOutboundLayerCount: number | null;
    maxInboundLayerCount: number | null;
    estBandwidthMultiplier: number;
    downscalerMode: DownscalerMode;
    senderBackend: SenderBackendKind;
}

export function getVideoDebugSettings(): VideoDebugSettings {
    return {
        forceH264Only: getForceH264OnlyImpl(),
        maxOutboundLayerCount: getLayerCount(OUTBOUND_LAYER_COUNT_KEY),
        maxInboundLayerCount: getLayerCount(INBOUND_LAYER_COUNT_KEY),
        estBandwidthMultiplier: getBandwidthMultiplier(),
        downscalerMode: getDownscalerModeImpl(),
        senderBackend: getSenderBackendModeImpl(),
    };
}

export function setVideoDebugForceH264Only(enabled: boolean): void {
    setForceH264OnlyImpl(enabled);
}

export function setVideoDebugDownscalerMode(mode: DownscalerMode): void {
    setDownscalerModeImpl(mode);
}

export function setVideoDebugSenderBackend(mode: SenderBackendKind): void {
    setSenderBackendModeImpl(mode);
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
