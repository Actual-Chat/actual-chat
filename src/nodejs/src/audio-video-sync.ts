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

export class AudioVideoSync {
    /** Called by AudioPlayer on every feeder state change, and from C# AudioTrackPlayer on MAUI */
    static update(authorId: string, playingAtSec: number, recordedAtMs: number, playbackState: string): void {
        // Terminal states — clear sync data so video falls back to wall-clock timing
        if (playbackState === 'ended') {
            this.clear(authorId);
            return;
        }

        const state: AudioSyncState = {
            playingAtSec,
            capturedAt: performance.now(),
            recordedAtMs,
            playbackState: playbackState as AudioPlaybackState,
        };
        registry.set(authorId, state);
        const now = state.capturedAt;
        if (now - lastLogTime > 1000) {
            lastLogTime = now;
            debugLog?.log(
                `update: authorId=${authorId}, playingAt=${playingAtSec.toFixed(2)}s, ` +
                `state=${playbackState}, recordedAtMs=${recordedAtMs.toFixed(0)}`);
        }
    }

    /** Called by AudioPlayer on end/reset, and from C# AudioTrackPlayer.OnEnded() on MAUI */
    static clear(authorId: string): void {
        debugLog?.log(`clear: authorId=${authorId}`);
        registry.delete(authorId);
    }

    /** Called by VideoPlayer in render loop */
    static get(authorId: string): AudioSyncState | undefined {
        return registry.get(authorId);
    }

    /** Extrapolate current playingAt for 'playing' state; returns seconds */
    static interpolatePlayingAt(state: AudioSyncState): number {
        if (state.playbackState !== 'playing') {
            return state.playingAtSec;
        }
        const elapsedSec = (performance.now() - state.capturedAt) / 1000;
        return state.playingAtSec + elapsedSec;
    }
}
