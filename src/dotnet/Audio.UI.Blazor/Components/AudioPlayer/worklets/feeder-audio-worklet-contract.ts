import { RpcNoWait } from 'rpc';

export interface FeederAudioWorklet {
    init(id: string, workerPort: MessagePort): Promise<void>;
    getState(): Promise<FeederState>;

    // Commands
    frame(buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void>;
    pause(): Promise<void>;
    resume(): Promise<void>;
    end(mustAbort: boolean, noWait?: RpcNoWait): Promise<void>;
}

export interface FeederAudioNode {
    stateChanged(state: PlaybackState, bufferState: BufferState, noWait?: RpcNoWait): Promise<void>;
}

export interface FeederState {
    /** Buffered duration in seconds  */
    bufferedDuration: number,
    playingAt: number,
    playbackState: PlaybackState,
    bufferState: BufferState,
}

export type BufferState = 'enough' | 'starving' | 'low';
export type PlaybackState = 'playing' | 'paused' | 'ended';
