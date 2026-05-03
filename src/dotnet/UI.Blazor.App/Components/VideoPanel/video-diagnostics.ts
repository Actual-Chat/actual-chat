import { getActiveRecorder, type OwnStreamDiagnostics } from './video-recorder';
import { getActivePlayers, recordRequestedReceiveQuality, type RemoteStreamDiagnostics } from './video-player';
import {
    getForceH264Only as getForceH264OnlyImpl,
    setForceH264Only as setForceH264OnlyImpl,
} from '../../Services/Video/codec-support';

export interface OwnStreamDiagnosticsSnapshot {
    stream: OwnStreamDiagnostics | null;
}

export function collectOwnStreamDiagnostics(kind: number): OwnStreamDiagnosticsSnapshot {
    const recorder = getActiveRecorder(kind);
    return {
        stream: recorder?.getDiagnostics() ?? null,
    };
}

export async function collectRemoteStreamDiagnostics(streamId: string): Promise<RemoteStreamDiagnostics | null> {
    const player = getActivePlayers().get(streamId);
    if (!player) return null;
    return player.getDiagnosticsAsync();
}

export async function collectActiveStreamHints(): Promise<{ streamId: string; currentSpatialLayerId: number }[]> {
    const result: { streamId: string; currentSpatialLayerId: number }[] = [];
    for (const [streamId, player] of getActivePlayers()) {
        try {
            const d = await player.getDiagnosticsAsync();
            const layer = d.forwarded?.ForwardedSpatialLayerId ?? 0;
            result.push({ streamId, currentSpatialLayerId: layer });
        } catch {
            result.push({ streamId, currentSpatialLayerId: 0 });
        }
    }
    return result;
}

export function setRequestedReceiveQuality(
    streamId: string,
    maxSpatialLayer: number | null,
    maxTemporalLayer: number | null
): void {
    if (maxSpatialLayer === null || maxTemporalLayer === null) {
        recordRequestedReceiveQuality(streamId, null);
        return;
    }

    recordRequestedReceiveQuality(streamId, { maxSpatialLayer, maxTemporalLayer });
}

// Diagnostic settings — toggleable from VideoDiagnosticsSettingsModal.
// Backed by localStorage; codec flags take effect on the next codec
// detection pass (typically the next stream).
export interface VideoDebugSettings {
    forceH264Only: boolean;
    maxOutboundLayerCount: number | null;
    maxInboundLayerCount: number | null;
}

export function getVideoDebugSettings(): VideoDebugSettings {
    return {
        forceH264Only: getForceH264OnlyImpl(),
        maxOutboundLayerCount: getLayerCount(OUTBOUND_LAYER_COUNT_KEY),
        maxInboundLayerCount: getLayerCount(INBOUND_LAYER_COUNT_KEY),
    };
}

export function setVideoDebugForceH264Only(enabled: boolean): void {
    setForceH264OnlyImpl(enabled);
}

export function setVideoDebugMaxOutboundLayerCount(layerCount: number | null): void {
    setLayerCount(OUTBOUND_LAYER_COUNT_KEY, layerCount);
}

export function setVideoDebugMaxInboundLayerCount(layerCount: number | null): void {
    setLayerCount(INBOUND_LAYER_COUNT_KEY, layerCount);
}

const OUTBOUND_LAYER_COUNT_KEY = 'video.debug.maxOutboundLayerCount';
const INBOUND_LAYER_COUNT_KEY = 'video.debug.maxInboundLayerCount';

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
