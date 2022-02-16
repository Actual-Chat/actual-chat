export interface EncoderResponseMessage {
    type: 'loadCompleted' | 'initCompleted' | 'doneCompleted';
    callbackId: number;
}
