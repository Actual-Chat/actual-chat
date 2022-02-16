export interface EncoderWorkletMessage {
    type: 'init' | 'buffer';
}

export interface BufferEncoderWorkletMessage extends EncoderWorkletMessage {
    type: 'buffer';
    buffer: ArrayBuffer;
}
