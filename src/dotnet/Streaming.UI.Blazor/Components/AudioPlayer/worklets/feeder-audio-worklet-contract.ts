import { RpcNoWait } from 'rpc';

export interface FeederAudioWorklet {
    init(id: string, workerPort: MessagePort): Promise<void>;

    // Commands
    frame(buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void>;
    pause(noWait?: RpcNoWait): Promise<void>;
    resume(): Promise<void>;
    end(mustAbort: boolean, noWait?: RpcNoWait): Promise<void>;
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

