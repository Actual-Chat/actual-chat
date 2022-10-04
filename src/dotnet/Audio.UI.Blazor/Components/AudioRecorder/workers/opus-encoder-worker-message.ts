export interface EncoderMessage {
    type: 'create' | 'init' | 'end';
    callbackId: number;
}

export interface CreateEncoderMessage extends EncoderMessage {
    type: 'create';
    audioHubUrl: string;
    debug: boolean;
}

export interface InitEncoderMessage extends EncoderMessage {
    type: 'init';
    channelCount: number;
    bitsPerSecond: number;
    sessionId: string;
    chatId: string;
}

export interface EndMessage extends EncoderMessage {
    type: 'end';
}
