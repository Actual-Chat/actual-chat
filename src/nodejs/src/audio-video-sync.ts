import { getLogs } from 'logging';

const { debugLog } = getLogs('AudioVideoSync');

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

// Wire-format messages pushed to subscribed worker ports.
// Workers re-anchor capturedAt to their own performance.now() on receipt
// because each worker has its own timeOrigin.
export type AudioSyncMessage =
    | { kind: 'state'; state: AudioSyncState }
    | { kind: 'clear' };

const registry = new Map<string, AudioSyncState>();
const workerPorts = new Map<string, Set<MessagePort>>();
const lastBroadcastAt = new Map<string, number>();
const lastBroadcastState = new Map<string, AudioPlaybackState>();
let lastLogTime = 0;

// Coalesce broadcasts to subscribed worker ports — at most one per 16 ms
// (≈ 60 Hz) per author. Workers interpolate playingAtSec themselves, so missing
// intermediate samples doesn't degrade A/V sync. State transitions
// (paused/starving/playing) bypass the rate limit so the worker reacts instantly.
const BROADCAST_MIN_INTERVAL_MS = 16;

function broadcast(authorId: string, message: AudioSyncMessage): void {
    const ports = workerPorts.get(authorId);
    if (!ports || ports.size === 0) return;
    for (const port of ports) {
        try {
            port.postMessage(message);
        } catch {
            // Port may be disconnected; drop silently.
        }
    }
}

function broadcastState(authorId: string, state: AudioSyncState): void {
    const now = state.capturedAt;
    const prevState = lastBroadcastState.get(authorId);
    const stateChanged = prevState !== state.playbackState;
    if (!stateChanged) {
        const last = lastBroadcastAt.get(authorId) ?? 0;
        if (now - last < BROADCAST_MIN_INTERVAL_MS) return;
    }
    lastBroadcastAt.set(authorId, now);
    lastBroadcastState.set(authorId, state.playbackState);
    broadcast(authorId, { kind: 'state', state });
}

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
        broadcastState(authorId, state);
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
        lastBroadcastAt.delete(authorId);
        lastBroadcastState.delete(authorId);
        broadcast(authorId, { kind: 'clear' });
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

    // Subscribes a worker-owned MessagePort to sync updates for one author.
    // The current state is delivered immediately (if any) so the worker doesn't
    // wait for the next audio tick to learn the clock.
    static subscribeWorker(authorId: string, port: MessagePort): void {
        let ports = workerPorts.get(authorId);
        if (!ports) {
            ports = new Set();
            workerPorts.set(authorId, ports);
        }
        ports.add(port);
        const current = registry.get(authorId);
        if (current) {
            try {
                port.postMessage({ kind: 'state', state: current } satisfies AudioSyncMessage);
            } catch {
                // ignore
            }
        }
    }

    static unsubscribeWorker(authorId: string, port: MessagePort): void {
        const ports = workerPorts.get(authorId);
        if (!ports) return;
        ports.delete(port);
        if (ports.size === 0) workerPorts.delete(authorId);
    }
}
