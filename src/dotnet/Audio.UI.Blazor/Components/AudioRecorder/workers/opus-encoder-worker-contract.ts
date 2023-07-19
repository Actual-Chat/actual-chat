import { VoiceActivityChange } from './audio-vad-contract';
import { RpcNoWait, RpcTimeout } from 'rpc';

export interface OpusEncoderWorker {
    create(artifactVersions: Map<string, string>, audioHubUrl: string, timeout?: RpcTimeout): Promise<void>;
    init(workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void>;
    start(secureToken: string, chatId: string, repliedChatEntryId: string): Promise<void>;
    updateSecureToken(secureToken: string, noWait?: RpcNoWait): Promise<void>;
    stop(): Promise<void>;
    reconnect(noWait?: RpcNoWait): Promise<void>;

    onEncoderWorkletSamples(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
    onVoiceActivityChange(change: VoiceActivityChange, noWait?: RpcNoWait): Promise<void>;
}
