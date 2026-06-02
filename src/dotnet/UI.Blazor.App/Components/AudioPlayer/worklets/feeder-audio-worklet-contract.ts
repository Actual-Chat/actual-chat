import { RpcNoWait } from 'rpc';
import type { AppConstants } from 'app-constants';
import type { SharedSettingsSnapshot } from 'shared-settings';
import type { SharedSettingsWorker } from 'shared-settings-worker';

export interface FeederAudioWorklet extends SharedSettingsWorker {
    init(appConstants: AppConstants, sharedSettings: SharedSettingsSnapshot, id: string, workerPort: MessagePort): Promise<void>;

    // Commands
    frame(
        buffer: ArrayBuffer,
        offset: number,
        length: number,
        sourceRecordedAtMs: number,
        sourceOffsetMs: number,
        noWait?: RpcNoWait): Promise<void>;
    pause(noWait?: RpcNoWait): Promise<void>;
    resume(preSkip: number): Promise<void>;
    end(mustAbort: boolean, noWait?: RpcNoWait): Promise<void>;
}

export interface FeederAudioWorkletEventHandler {
    onStateChanged(state: FeederState, noWait?: RpcNoWait): Promise<void>;
}

export interface FeederState {
    playbackState: PlaybackState,
    bufferState: BufferState,
    playingAt: number,
    presentationLagMs: number | null,
    bufferedDuration: number,
}

export type BufferState = 'low' | 'ok';
export type PlaybackState = 'playing' | 'paused' | 'ended' | 'starving';
