export type VoiceActivityKind = 'start' | 'end';

export interface VoiceActivityChange {
    kind: VoiceActivityKind;
    offset: number;
    duration?: number;
    speechProb: number;
}

export interface VoiceActivityDetector {
    lastActivityEvent: VoiceActivityChange;

    init(): Promise<void>;
    reset(): void;
    conversationSignal(): void;
    appendChunk(monoPcm: Float32Array): Promise<VoiceActivityChange | number> ;
}
