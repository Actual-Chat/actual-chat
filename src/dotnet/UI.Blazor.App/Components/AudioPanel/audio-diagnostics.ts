import { AudioPlayer, type AudioPlayerDiagnostics } from '../AudioPlayer/audio-player';
import { audioContextSource, type AudioContextSourceDiagnostics } from '../../Services/audio-context-source';

export interface AudioPlaybackDiagnostics {
    // Null on the MAUI app, where playback is native and there is no Web Audio context.
    context: AudioContextSourceDiagnostics | null;
    players: AudioPlayerDiagnostics[];
}

export function collectAudioPlaybackDiagnostics(): AudioPlaybackDiagnostics {
    return {
        context: audioContextSource ? audioContextSource.getDiagnostics() : null,
        players: AudioPlayer.collectDiagnostics(),
    };
}

// Actions-tab lever: force an interactive resume of the playback AudioContext.
// Best-effort — iOS Safari may still refuse outside a real user gesture; no-op on MAUI.
export async function audioDebugResumeContext(): Promise<void> {
    if (audioContextSource)
        await audioContextSource.initContextInteractively();
}
