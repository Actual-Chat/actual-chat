import { VoiceActivityChange } from './audio-vad-contract';
import { AudioDiagnosticsState } from "../audio-recorder";
import { RpcNoWait, RpcTimeout } from 'rpc';

export interface OpusEncoderWorker {
    create(artifactVersions: Map<string, string>, hubUrl: string, timeout?: RpcTimeout): Promise<void>;
    init(workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void>;
    start(chatId: string, repliedChatEntryId: string): Promise<void>;
    setSessionToken(sessionToken: string, noWait?: RpcNoWait): Promise<void>;
    stop(): Promise<void>;
    reconnect(noWait?: RpcNoWait): Promise<void>;
    disconnect(noWait?: RpcNoWait): Promise<void>;
    runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState>;

    onEncoderWorkletSamples(buffer: ArrayBuffer): Promise<void>;
    onVoiceActivityChange(change: VoiceActivityChange, noWait?: RpcNoWait): Promise<void>;
}
