import { getActiveRecorder, type OwnStreamDiagnostics } from './video-recorder';
import { getActivePlayers, type RemoteStreamDiagnostics } from './video-player';
import {
    getForceH264Only as getForceH264OnlyImpl,
    setForceH264Only as setForceH264OnlyImpl,
} from '../../Services/Video/codec-support';
import { AudioVideoSync } from 'audio-video-sync';

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

// Diagnostic settings — toggleable from VideoDiagnosticsSettingsModal.
// Backed by localStorage; codec flags take effect on the next codec
// detection pass (typically the next stream), avSyncEnabled takes effect
// on the next render tick.
export interface VideoDebugSettings {
    forceH264Only: boolean;
    avSyncEnabled: boolean;
}

export function getVideoDebugSettings(): VideoDebugSettings {
    return {
        forceH264Only: getForceH264OnlyImpl(),
        avSyncEnabled: AudioVideoSync.isEnabled,
    };
}

export function setVideoDebugForceH264Only(enabled: boolean): void {
    setForceH264OnlyImpl(enabled);
}

export function setVideoDebugAvSyncEnabled(enabled: boolean): void {
    AudioVideoSync.setEnabled(enabled);
}
