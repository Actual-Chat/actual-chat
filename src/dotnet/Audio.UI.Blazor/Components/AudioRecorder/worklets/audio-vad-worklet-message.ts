export interface VadWorkletMessage {
    type: 'init' | 'buffer';
}

export interface BufferVadWorkletMessage extends VadWorkletMessage {
    type: 'buffer';
    buffer: ArrayBuffer;
}
