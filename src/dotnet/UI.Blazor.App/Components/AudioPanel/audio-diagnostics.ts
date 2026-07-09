import { AudioPlayer, type AudioPlayerDiagnostics } from '../AudioPlayer/audio-player';
import { audioContextSource, type AudioContextSourceDiagnostics } from '../../Services/audio-context-source';

export interface AudioPlaybackDiagnostics {
    context: AudioContextSourceDiagnostics;
    players: AudioPlayerDiagnostics[];
}

export function collectAudioPlaybackDiagnostics(): AudioPlaybackDiagnostics {
    return {
        context: audioContextSource.getDiagnostics(),
        players: AudioPlayer.collectDiagnostics(),
    };
}

// Actions-tab lever: force an interactive resume of the playback AudioContext.
// Best-effort — iOS may still refuse outside a real user gesture.
export async function audioDebugResumeContext(): Promise<void> {
    await audioContextSource.initContextInteractively();
}
