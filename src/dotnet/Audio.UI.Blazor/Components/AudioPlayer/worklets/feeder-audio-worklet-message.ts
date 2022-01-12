/** Message that is used to communicate between the global scope and the audio worklet scope (node -> processor) */
export interface NodeMessage {
    type: "init" | "data" | "changeState" | "clear" | "getState";
}

/** Message that is used to communicate between the audio worklet scope and the global scope (processor -> node) */
export interface ProcessorMessage {
    type: "stateChanged" | "state";
}

export interface InitNodeMessage extends NodeMessage {
    type: 'init';
    decoderWorkerPort: MessagePort;
}

export interface DataNodeMessage extends NodeMessage {
    type: 'data';
    buffer: ArrayBuffer;
}

export interface GetStateNodeMessage extends NodeMessage {
    type: 'getState';
    id: number;
}

export interface ChangeStateNodeMessage extends NodeMessage {
    type: 'changeState';
    state: "play" | "stop";
}

export interface StateProcessorMessage extends ProcessorMessage {
    type: "state",
    id: number,
    /** Buffered samples duration in seconds  */
    bufferedTime: number,
    /** In seconds from the start of playing */
    playbackTime: number,

}

export interface StateChangedProcessorMessage extends ProcessorMessage {
    type: "stateChanged";
    state: "playing" | "playingWithLowBuffer" | "playingWithTooMuchBuffer" | "starving" | "stopped";
}
