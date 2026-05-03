import { VoiceActivityChange } from './audio-vad-contract';
import { AudioDiagnosticsState } from '../audio-recorder';
import { RpcNoWait, RpcTimeout } from 'rpc';
import type { AppConstants } from 'app-constants';
import type { SharedSettingsSnapshot } from 'shared-settings';
import type { SharedSettingsWorker } from 'shared-settings-worker';

export interface OpusEncoderWorker extends SharedSettingsWorker {
    create(appConstants: AppConstants, artifactVersions: Map<string, string>, sharedSettings: SharedSettingsSnapshot, apiUrl: string, timeout?: RpcTimeout): Promise<void>;
    init(workletMessagePort: MessagePort, vadMessagePort: MessagePort): Promise<void>;
    start(chatId?: string, repliedChatEntryId?: string): Promise<void>;
    stop(): Promise<void>;
    heartbeat(noWait?: RpcNoWait): Promise<void>;
    ensureConnected(quickReconnect: boolean, noWait?: RpcNoWait): Promise<void>;
    /** Debug-only: force-remove the worker's RPC peer from the hub. The reconnect
     *  loop will re-create it. Invoked by {@link DebugUI.disconnectApi}. */
    disconnectApi(noWait?: RpcNoWait): Promise<void>;
    /** Debug-only: bias the recorder's reported source start timestamp
     *  (`clientStartOffset` in the legacy RPC contract) by `offsetMs` ms on
     *  every new PushStream. Used to simulate
     *  audio drift relative to real time so the audio catch-up policy can be
     *  exercised end-to-end. Invoked by {@link DebugUI.setAudioRecorderOffset}. */
    setRecorderOffset(offsetMs: number, noWait?: RpcNoWait): Promise<void>;
    runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState>;

    onEncoderWorkletSamples(buffer: ArrayBuffer, capturedAtMs: number, noWait?: RpcNoWait): Promise<void>;
    onVoiceActivityChange(change: VoiceActivityChange, noWait?: RpcNoWait): Promise<void>;
    onConnectivityUpdate(isOnline: boolean, isConnected: boolean, isBlazorServer: boolean, noWait?: RpcNoWait): Promise<void>;
}
