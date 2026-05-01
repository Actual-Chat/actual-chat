import { RpcNoWait } from 'rpc';
import type { AppConstants } from 'app-constants';

export interface FeederAudioWorklet {
    init(appConstants: AppConstants, id: string, workerPort: MessagePort): Promise<void>;

    // Commands
    frame(buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void>;
    pause(noWait?: RpcNoWait): Promise<void>;
    resume(preSkip: number): Promise<void>;
    end(mustAbort: boolean, noWait?: RpcNoWait): Promise<void>;
    setBufferEscalation(value: number, noWait?: RpcNoWait): Promise<void>;
}

export interface FeederAudioWorkletEventHandler {
    onStateChanged(state: FeederState, noWait?: RpcNoWait): Promise<void>;
}

export interface FeederState {
    playbackState: PlaybackState,
    bufferState: BufferState,
    playingAt: number,
    bufferedDuration: number,
}

export type BufferState = 'low' | 'ok';
export type PlaybackState = 'playing' | 'paused' | 'ended' | 'starving';

