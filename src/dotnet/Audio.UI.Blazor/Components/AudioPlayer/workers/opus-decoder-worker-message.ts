/* Message that is used to communicate between the global scope and the decoder web worker (main thread -> worker) */
export interface DecoderMessage {
    type: 'create' | 'init' | 'data' | 'end' | 'stop';
}

/** When controller created he sends the create message to initialize decoder on the web worker side */
export interface CreateDecoderMessage extends DecoderMessage {
    type: 'create';
    controllerId: number;
    callbackId: number;
    workletPort: MessagePort;
    artifactVersions: Map<string,string>;
}

export interface InitDecoderMessage extends DecoderMessage {
    type: 'init';
    controllerId: number,
    callbackId: number,
}

export interface DataDecoderMessage extends DecoderMessage {
    type: 'data';
    controllerId: number;
    // ArrayBuffer can be bigger than Uint8Array and can be started not from the beginning
    // so we should transfer offset and length too
    buffer: ArrayBuffer;
    offset: number;
    length: number;
}

export interface EndDecoderMessage extends DecoderMessage {
    type: 'end';
    controllerId: number;
}

export interface StopDecoderMessage extends DecoderMessage {
    type: 'stop';
    controllerId: number;
}

/** Message that is sent from the decoder web worker (web worker -> { worklet | main thread }) */
export interface DecoderWorkerMessage {
    type: 'end' | 'samples' | 'operationCompleted';
}

/** Decoded samples, will be consumed at the decoder worklet (web worker -> worklet) */
export interface SamplesDecoderWorkerMessage extends DecoderWorkerMessage {
    type: 'samples';
    buffer: ArrayBuffer;
    offset: number;
    length: number;
}
/** Message says that decoder has reached the end of the stream (web worker -> worklet) */
export interface EndDecoderWorkerMessage extends DecoderWorkerMessage {
    type: 'end';
}

/** Tells that an (async) operation was completed (create, init etc). (web worker -> main thread) */
export interface OperationCompletedDecoderWorkerMessage extends DecoderWorkerMessage {
    type: 'operationCompleted';
    callbackId: number;
}
