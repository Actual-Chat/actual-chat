import { VoiceActivityChange } from './audio-vad';

export interface OpusEncoderWorker {
    create(audioHubUrl: string): Promise<void>;
    init(workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void>;
    start(sessionId: string, chatId: string): Promise<void>;
    stop(): Promise<void>;

    onEncoderWorkletOutput(buffer: ArrayBuffer): Promise<void>;
    onVoiceActivityChange(change: VoiceActivityChange): Promise<void>;
}
