export type EncoderMessageType = 'loadEncoder' | 'init' | 'done';

export interface EncoderMessage {
    type: EncoderMessageType;
}

export interface LoadEncoderMessage extends EncoderMessage {
    type: 'loadEncoder';
    mimeType: 'audio/webm';
    wasmPath: string;
    audioHubUrl: string;
}

export interface InitMessage extends EncoderMessage {
    type: 'init';
    sampleRate: 48000;
    channelCount: number;
    bitsPerSecond: number;
    sessionId: string;
    chatId: string;
}

export interface DoneMessage extends EncoderMessage {
    type: 'done';
}
