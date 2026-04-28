import { getActiveRecorder, type OwnStreamDiagnostics } from './video-recorder';
import { getActivePlayers, type RemoteStreamDiagnostics } from './video-player';

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
