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
//
// Encoded as a flat tuple of primitives — avoids the structured-clone cost
// of an object with named fields and an enum string. The sync update fires
// at ~60 Hz per subscribed worker, so trimming the per-tick clone has a
// measurable cumulative benefit on long calls.
//
// Tuple shape:
//   [0]: kind         — 0 = clear, 1 = state
//   [1]: playingAtSec
//   [2]: capturedAt   (sender-side performance.now(); receiver re-anchors)
//   [3]: recordedAtMs
//   [4]: playbackState code — see AUDIO_PLAYBACK_STATE_CODES
export const AUDIO_SYNC_KIND_CLEAR = 0;
export const AUDIO_SYNC_KIND_STATE = 1;

export const AUDIO_PLAYBACK_STATE_CODES = {
    playing: 0,
    paused: 1,
    starving: 2,
    // 'ended' is not sent — it triggers AUDIO_SYNC_KIND_CLEAR instead
} as const satisfies Record<Exclude<AudioPlaybackState, 'ended'>, number>;

const PLAYBACK_STATE_FROM_CODE: Record<number, AudioPlaybackState> = {
    0: 'playing',
    1: 'paused',
    2: 'starving',
};

export function decodePlaybackState(code: number): AudioPlaybackState {
    return PLAYBACK_STATE_FROM_CODE[code] ?? 'paused';
}

export type AudioSyncMessage =
    | readonly [kind: typeof AUDIO_SYNC_KIND_CLEAR]
    | readonly [
        kind: typeof AUDIO_SYNC_KIND_STATE,
        playingAtSec: number,
        capturedAt: number,
        recordedAtMs: number,
        playbackStateCode: number,
    ];

const registry = new Map<string, AudioSyncState>();
const workerPorts = new Map<string, Set<MessagePort>>();
const lastBroadcastAt = new Map<string, number>();
const lastBroadcastState = new Map<string, AudioPlaybackState>();
let lastLogTime = 0;

// A/V sync is opt-in. When disabled, AudioVideoSync.update() is a no-op,
// so registry stays empty and consumers fall back to wall-clock targeting.
// Toggled from VideoDiagnosticsSettingsModal; persisted in localStorage.
const AV_SYNC_ENABLED_KEY = 'video.debug.avSyncEnabled';
let isEnabledCached: boolean | null = null;

function readEnabledFromStorage(): boolean {
    try {
        return globalThis.localStorage.getItem(AV_SYNC_ENABLED_KEY) === 'true';
    } catch {
        // localStorage access can throw in private mode / sandboxed contexts.
        return false;
    }
}

function getEnabled(): boolean {
    isEnabledCached ??= readEnabledFromStorage();
    return isEnabledCached;
}

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

function encodeStateMessage(state: AudioSyncState): AudioSyncMessage {
    // 'ended' never reaches broadcastState (handled by AudioVideoSync.update
    // which routes to clear()); the cast is safe.
    const code = AUDIO_PLAYBACK_STATE_CODES[
        state.playbackState as Exclude<AudioPlaybackState, 'ended'>
    ];
    return [AUDIO_SYNC_KIND_STATE, state.playingAtSec, state.capturedAt, state.recordedAtMs, code];
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
    broadcast(authorId, encodeStateMessage(state));
}

export class AudioVideoSync {
    /** Whether A/V sync is currently active. Off by default; opt in via diagnostics. */
    static get isEnabled(): boolean {
        return getEnabled();
    }

    /**
     * Enable/disable A/V sync globally. Persists to localStorage.
     * On disable, drops all sync state and broadcasts CLEAR to subscribed
     * workers so the wall-clock fallback engages immediately.
     */
    static setEnabled(value: boolean): void {
        if (getEnabled() === value) return;
        isEnabledCached = value;
        try {
            if (value) globalThis.localStorage.setItem(AV_SYNC_ENABLED_KEY, 'true');
            else globalThis.localStorage.removeItem(AV_SYNC_ENABLED_KEY);
        } catch (e) {
            debugLog?.log(`setEnabled: localStorage write failed: ${String(e)}`);
        }
        debugLog?.log(`setEnabled: ${value}`);
        if (!value) {
            for (const authorId of [...registry.keys()]) {
                this.clear(authorId);
            }
        }
    }

    /** Called by AudioPlayer on every feeder state change, and from C# AudioTrackPlayer on MAUI */
    static update(authorId: string, playingAtSec: number, recordedAtMs: number, playbackState: string): void {
        if (!getEnabled()) return;

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
        broadcast(authorId, [AUDIO_SYNC_KIND_CLEAR]);
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
                port.postMessage(encodeStateMessage(current));
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
