import { AudioPlayer, type AudioPlayerDiagnostics } from '../AudioPlayer/audio-player';
import { audioContextSource, type AudioContextSource, type AudioContextSourceDiagnostics } from '../../Services/audio-context-source';

export interface AudioPlaybackDiagnostics {
    // Null on the MAUI app, where playback is native and there is no Web Audio context.
    context: AudioContextSourceDiagnostics | null;
    players: AudioPlayerDiagnostics[];
}

// audioContextSource is `null!` on the MAUI app (native playback), so it must be
// treated as nullable despite its declared type.
export function collectAudioPlaybackDiagnostics(): AudioPlaybackDiagnostics {
    const source = audioContextSource as AudioContextSource | null;
    return {
        context: source ? source.getDiagnostics() : null,
        players: AudioPlayer.collectDiagnostics(),
    };
}

// Actions-tab lever: force an interactive resume of the playback AudioContext.
// Best-effort — iOS Safari may still refuse outside a real user gesture; no-op on MAUI.
export async function audioDebugResumeContext(): Promise<void> {
    const source = audioContextSource as AudioContextSource | null;
    if (source)
        await source.initContextInteractively();
}
