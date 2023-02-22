export interface EncoderMessage {
    type: 'create' | 'init' | 'start' | 'end';
    rpcResultId: number;
}

export interface CreateEncoderMessage extends EncoderMessage {
    type: 'create';
    audioHubUrl: string;
    artifactVersions: Map<string,string>;
}

export interface InitEncoderMessage extends EncoderMessage {
    type: 'init';
}

export interface StartMessage extends EncoderMessage {
    type: 'start';
    sessionId: string;
    chatId: string;
}

export interface EndMessage extends EncoderMessage {
    type: 'end';
}
