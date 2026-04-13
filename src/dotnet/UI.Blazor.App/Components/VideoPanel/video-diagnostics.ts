import { getActiveRecorder, type OwnStreamDiagnostics } from './video-recorder';
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
