import { Log } from 'logging';

const { debugLog } = Log.get('AudioVideoSync');

/** Playback state from the audio feeder worklet */
export type AudioPlaybackState = 'playing' | 'paused' | 'ended' | 'starving';

export interface AudioSyncState {
    /** Current audio playingAt in seconds (from feeder worklet) */
    playingAtSec: number;
    /** performance.now() when this state was captured */
    capturedAt: number;
    /** RecordedAt of the audio track in ms since epoch */
    recordedAtMs: number;
    /** Current playback state */
    playbackState: AudioPlaybackState;
}

const registry = new Map<string, AudioSyncState>();
let lastLogTime = 0;

/** Called by AudioPlayer on every feeder state change */
export function updateAudioSyncState(authorId: string, state: AudioSyncState): void {
    registry.set(authorId, state);
    const now = performance.now();
    if (now - lastLogTime > 1000) {
        lastLogTime = now;
        debugLog?.log(
            `updateAudioSyncState: authorId=${authorId}, playingAt=${state.playingAtSec.toFixed(2)}s, ` +
            `state=${state.playbackState}, recordedAtMs=${state.recordedAtMs.toFixed(0)}`);
    }
}

/** Called by AudioPlayer on end/reset */
export function clearAudioSyncState(authorId: string): void {
    debugLog?.log(`clearAudioSyncState: authorId=${authorId}`);
    registry.delete(authorId);
}

/** Called by VideoPlayer in render loop */
export function getAudioSyncState(authorId: string): AudioSyncState | undefined {
    return registry.get(authorId);
}

/** Extrapolate current playingAt for 'playing' state; returns seconds */
export function interpolatePlayingAt(state: AudioSyncState): number {
    if (state.playbackState !== 'playing') {
        // Not playing — return last known position without extrapolation
        return state.playingAtSec;
    }
    const elapsedSec = (performance.now() - state.capturedAt) / 1000;
    return state.playingAtSec + elapsedSec;
}
