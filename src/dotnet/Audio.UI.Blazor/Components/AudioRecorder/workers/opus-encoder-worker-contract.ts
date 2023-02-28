import { VoiceActivityChange } from './audio-vad';
import { RpcNoWait } from 'rpc';

export interface OpusEncoderWorker {
    create(artifactVersions: Map<string, string>, audioHubUrl: string): Promise<void>;
    init(workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void>;
    start(sessionId: string, chatId: string, repliedChatEntryId: string): Promise<void>;
    stop(): Promise<void>;

    onEncoderWorkletSamples(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
    onVoiceActivityChange(change: VoiceActivityChange, noWait?: RpcNoWait): Promise<void>;
}
