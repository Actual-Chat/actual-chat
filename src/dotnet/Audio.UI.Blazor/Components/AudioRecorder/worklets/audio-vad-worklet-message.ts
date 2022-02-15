export interface VadWorkletMessage {
    type: 'init-port' | 'buffer';
}

export interface BufferVadWorkletMessage extends VadWorkletMessage {
    type: 'buffer';
    buffer: ArrayBuffer;
}
