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

// Lightweight per-stream hint for the playback override path. Returns the
// stream IDs of all currently-active players + the most recently forwarded
// spatial layer ID per stream (default 0 if no frames seen yet). The settings
// modal calls this when the user picks an override mode so SetPlaybackOverride
// can pin every active stream in a single push.
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

// Updates the per-stream "requested ReceiveQuality" registry consulted by
// VideoPlayer.getDiagnosticsAsync(). Called from C# (VideoQualityUI) right
// before / after a ChangePlaybackQuality push so the modal can show what the
// client is asking for. Pass null to clear an entry.
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
}

export function getVideoDebugSettings(): VideoDebugSettings {
    return {
        forceH264Only: getForceH264OnlyImpl(),
    };
}

export function setVideoDebugForceH264Only(enabled: boolean): void {
    setForceH264OnlyImpl(enabled);
}
