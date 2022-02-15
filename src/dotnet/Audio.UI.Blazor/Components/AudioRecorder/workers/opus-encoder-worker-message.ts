export type EncoderMessageType = 'load-module' | 'init-new-stream' | 'done';

export interface EncoderMessage {
    type: EncoderMessageType;
}

export interface LoadModuleMessage extends EncoderMessage {
    type: 'load-module';
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
    debugMode: boolean;
}

export interface DoneMessage extends EncoderMessage {
    type: 'done';
}
