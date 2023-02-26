import { RpcNoWait } from 'rpc';

export interface FeederAudioWorklet {
    init(workerPort: MessagePort): Promise<void>;
    getState(): Promise<PlaybackState>;
    stop(): Promise<void>;
    pause(): Promise<void>;
    resume(): Promise<void>;

    onSamples(buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void>;
    onEnd(noWait?: RpcNoWait): Promise<void>;
}

export interface FeederAudioNode {
    onStateUpdated(state: ProcessorState, noWait?: RpcNoWait): Promise<void>;
}

export interface PlaybackState {
    /** Buffered samples duration in seconds  */
    bufferedTime: number,
    /** In seconds from the start of playing, excluding starving time and processing time */
    playbackTime: number,
}

export type ProcessorState = 'playing' | 'playingWithLowBuffer' | 'playingWithTooMuchBuffer' | 'starving' | 'paused' | 'resumed' | 'stopped' | 'ended';
