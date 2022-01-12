/* Message that is used to communicate between the global scope and the decoder web worker (main thread -> worker) */
export interface DecoderMessage {
    type: 'load' | 'init' | 'data' | 'end' | 'stop';
}

export interface LoadDecoderMessage extends DecoderMessage {
    type: 'load';
}

export interface InitDecoderMessage extends DecoderMessage {
    type: 'init';
    playerId: string;
    buffer: ArrayBuffer;
    offset: number;
    length: number;
    workletPort: MessagePort;
}

export interface DataDecoderMessage extends DecoderMessage {
    type: 'data';
    playerId: string;
    buffer: ArrayBuffer;
    offset: number;
    length: number;
}

export interface EndDecoderMessage extends DecoderMessage {
    type: 'end';
    playerId: string;
}

export interface StopDecoderMessage extends DecoderMessage {
    type: 'stop';
    playerId: string;
}

/** Message that is sent from the decoder web worker (web worker -> { worklet | main thread }) */
export interface DecoderWorkerMessage {
    type: 'samples' | 'initCompleted';
}

/** Decoded samples, will be consumed at the decoder worklet (web worker -> worklet) */
export interface SamplesDecoderWorkerMessage extends DecoderWorkerMessage {
    type: 'samples';
    offset: number;
    length: number;
    buffer: ArrayBuffer;
}

/** Init callback message, handled at the audio player main thread (web worker -> main thread) */
export interface InitCompletedDecoderWorkerMessage extends DecoderWorkerMessage {
    type: 'initCompleted';
    playerId: string;
}

/** Processed buffer to be returned back from the worklet to the decoder worker (worklet -> web worker) */
export interface DecoderWorkletMessage {
    type: 'buffer';
    buffer: ArrayBuffer;
}
