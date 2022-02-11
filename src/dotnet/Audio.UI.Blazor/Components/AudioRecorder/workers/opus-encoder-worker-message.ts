export type EncoderMessageType = 'load-encoder' | 'init-new-stream' | 'done';

export interface EncoderMessage {
    type: EncoderMessageType;
}

export interface LoadEncoderMessage extends EncoderMessage {
    type: 'load-encoder';
    mimeType: 'audio/webm';
    wasmPath: string;
    audioHubUrl: string;
}

export interface InitNewStreamMessage extends EncoderMessage {
    type: 'init-new-stream';
    sampleRate: 48000;
    channelCount: number;
    bitsPerSecond: number;
    sessionId: string;
    chatId: string;
}

export interface DoneMessage extends EncoderMessage {
    type: 'done';
}
