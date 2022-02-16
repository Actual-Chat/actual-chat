export interface EncoderMessage {
    type: 'load' | 'init' | 'done';
    callbackId: number;
}

export interface LoadModuleMessage extends EncoderMessage {
    type: 'load';
    mimeType: 'audio/webm';
    wasmPath: string;
    audioHubUrl: string;
}

export interface InitNewStreamMessage extends EncoderMessage {
    type: 'init';
    channelCount: number;
    bitsPerSecond: number;
    sessionId: string;
    chatId: string;
    debugMode: boolean;
}

export interface DoneMessage extends EncoderMessage {
    type: 'done';
}
