export type VoiceActivityKind = 'start' | 'end';

export interface VoiceActivityChange {
    kind: VoiceActivityKind;
    offset: number;
    duration?: number;
    speechProb: number;
}

export interface VoiceActivityDetector {
    appendChunk(monoPcm: Float32Array): Promise<VoiceActivityChange | number> ;
    init(): Promise<void>;
    reset(): void;
    conversationSignal(): void;
}
