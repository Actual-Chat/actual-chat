// TODO remove these message types at all ?
export interface EncoderWorkletMessage {
    type: 'init' | 'buffer';
}
// TODO: add offset / length to the message (?)
/** encoder web worker -> to the worklet thread */
export interface BufferEncoderWorkletMessage extends EncoderWorkletMessage {
    type: 'buffer';
    buffer: ArrayBuffer;
}
