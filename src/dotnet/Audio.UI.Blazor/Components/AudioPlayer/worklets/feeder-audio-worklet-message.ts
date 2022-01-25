/** Message that is used to communicate between the global scope and the audio worklet scope (node -> processor) */
export interface NodeMessage {
    type: "init" | "stop" | "getState";
}

/** Message that is used to communicate between the audio worklet scope and the global scope (processor -> node) */
export interface ProcessorMessage {
    type: "stateChanged" | "state" | "operationCompleted";
}

/** Wires up the web worker and the worklet (node -> processor) */
export interface InitNodeMessage extends NodeMessage {
    type: 'init';
    callbackId: number;
    decoderWorkerPort: MessagePort;
}

export interface GetStateNodeMessage extends NodeMessage {
    type: 'getState';
    callbackId: number;
}

export interface StopNodeMessage extends NodeMessage {
    type: 'stop';
}

export interface StateProcessorMessage extends ProcessorMessage {
    type: "state",
    callbackId: number,
    /** Buffered samples duration in seconds  */
    bufferedTime: number,
    /** In seconds from the start of playing, excluding starving time and processing time */
    playbackTime: number,
}

export interface OperationCompletedProcessorMessage extends ProcessorMessage {
    type: 'operationCompleted';
    callbackId: number;
}

export interface StateChangedProcessorMessage extends ProcessorMessage {
    type: "stateChanged";
    state: "playing" | "playingWithLowBuffer" | "playingWithTooMuchBuffer" | "starving" | "stopped" | "ended";
}
