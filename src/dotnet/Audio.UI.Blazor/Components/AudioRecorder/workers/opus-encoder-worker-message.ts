export interface EncoderMessage {
    type: 'create' | 'init' | 'end';
    rpcResultId: number;
}

export interface CreateEncoderMessage extends EncoderMessage {
    type: 'create';
    audioHubUrl: string;
}

export interface InitEncoderMessage extends EncoderMessage {
    type: 'init';
    sessionId: string;
    chatId: string;
}

export interface EndMessage extends EncoderMessage {
    type: 'end';
}
