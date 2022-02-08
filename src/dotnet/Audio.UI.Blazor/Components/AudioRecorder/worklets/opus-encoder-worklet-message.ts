export interface EncoderWorkletMessage {
    type: 'init-port' | 'buffer';
}

export interface BufferEncoderWorkletMessage extends EncoderWorkletMessage {
    type: 'buffer';
    buffer: ArrayBuffer;
}
