import { VoiceActivityChange } from './audio-vad-contract';
import { AudioDiagnosticsState } from '../audio-recorder';
import { RpcNoWait, RpcTimeout } from 'rpc';

export interface OpusEncoderWorker {
    create(artifactVersions: Map<string, string>, apiUrl: string, timeout?: RpcTimeout): Promise<void>;
    init(workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void>;
    start(chatId?: string, repliedChatEntryId?: string): Promise<void>;
    stop(): Promise<void>;
    heartbeat(noWait?: RpcNoWait): Promise<void>;
    ensureConnected(quickReconnect: boolean, noWait?: RpcNoWait): Promise<void>;
    /** Debug-only: force-remove the worker's RPC peer from the hub. The reconnect
     *  loop will re-create it. Invoked by {@link DebugUI.disconnectApi}. */
    disconnectApi(noWait?: RpcNoWait): Promise<void>;
    runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState>;

    onEncoderWorkletSamples(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
    onVoiceActivityChange(change: VoiceActivityChange, noWait?: RpcNoWait): Promise<void>;
    onConnectivityUpdate(isOnline: boolean, isConnected: boolean, isBlazorServer: boolean, noWait?: RpcNoWait): Promise<void>;
    updateServerClockOffset(offsetMs: number, noWait?: RpcNoWait): Promise<void>;
}
