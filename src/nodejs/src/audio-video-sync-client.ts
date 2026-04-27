import type { AudioSyncMessage, AudioSyncState } from 'audio-video-sync';

// Worker-side mirror of AudioVideoSync state. Receives updates over a
// MessagePort opened by the main thread via AudioVideoSync.subscribeWorker.
//
// Each worker has its own timeOrigin, so capturedAt from the main thread
// is not directly usable. We re-anchor capturedAt to the worker's own
// performance.now() on receipt — this loses the IPC transit delay (~1 ms,
// well below a frame at 30 fps) but keeps subsequent interpolation arithmetic
// internally consistent.
export class AudioVideoSyncClient {
    private state: AudioSyncState | null = null;

    constructor(port: MessagePort, onChange?: () => void) {
        port.onmessage = (ev: MessageEvent<AudioSyncMessage>): void => {
            const msg = ev.data;
            if (msg.kind === 'state') {
                this.state = { ...msg.state, capturedAt: performance.now() };
            } else {
                this.state = null;
            }
            if (onChange) onChange();
        };
        port.start();
    }

    get(): AudioSyncState | null {
        return this.state;
    }

    interpolatePlayingAt(): number {
        const state = this.state;
        if (!state) return 0;
        if (state.playbackState !== 'playing') return state.playingAtSec;
        return state.playingAtSec + (performance.now() - state.capturedAt) / 1000;
    }
}
