/** Message that is used to communicate between the global scope and the audio worklet scope (node -> processor) */
export interface NodeMessage {
    type: "data" | "changeState" | "clear" | "getState";
}

/** Message that is used to communicate between the audio worklet scope and the global scope (processor -> node) */
export interface ProcessorMessage {
    type: "stateChanged" | "state";
}


export interface DataNodeMessage extends NodeMessage {
    buffer: ArrayBuffer;
}

export interface GetStateNodeMessage extends NodeMessage {
    id: number;
}

export interface ChangeStateNodeMessage extends NodeMessage {
    state: "play" | "stop";
}

export interface StateProcessorMessage extends ProcessorMessage {
    sampleCount: number,
    id: number,
    /** In milliseconds from the start of playing */
    playbackTime: number,

}

export interface StateChangedProcessorMessage extends ProcessorMessage {
    state: "playing" | "playingWithLowBuffer" | "starving" | "stopped";
}