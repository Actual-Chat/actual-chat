import { VoiceActivityChange } from './audio-vad-contract';
import { RpcNoWait, RpcTimeout } from 'rpc';
import {AudioDiagnosticsState} from "../audio-recorder";

export interface OpusEncoderWorker {
    create(artifactVersions: Map<string, string>, audioHubUrl: string, timeout?: RpcTimeout): Promise<void>;
    init(workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void>;
    start(chatId: string, repliedChatEntryId: string): Promise<void>;
    setSessionToken(sessionToken: string, noWait?: RpcNoWait): Promise<void>;
    stop(): Promise<void>;
    reconnect(noWait?: RpcNoWait): Promise<void>;
    disconnect(noWait?: RpcNoWait): Promise<void>;
    runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState>;

    onEncoderWorkletSamples(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
    onVoiceActivityChange(change: VoiceActivityChange, noWait?: RpcNoWait): Promise<void>;
}
