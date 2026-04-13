import { getActiveRecorder, type OwnStreamDiagnostics } from './video-recorder';
import { getActivePlayers, type RemoteStreamDiagnostics } from './video-player';
import { VideoStreamer } from '../../Services/Video/video-streamer';

export interface OwnStreamDiagnosticsSnapshot {
    stream: OwnStreamDiagnostics | null;
    signalRState: string;
}

export function collectOwnStreamDiagnostics(): OwnStreamDiagnosticsSnapshot {
    const recorder = getActiveRecorder();
    return {
        stream: recorder?.getDiagnostics() ?? null,
        signalRState: VideoStreamer.connection?.state ?? 'None',
    };
}

export async function collectRemoteStreamDiagnostics(streamId: string): Promise<RemoteStreamDiagnostics | null> {
    const player = getActivePlayers().get(streamId);
    if (!player) return null;
    return player.getDiagnosticsAsync();
}
