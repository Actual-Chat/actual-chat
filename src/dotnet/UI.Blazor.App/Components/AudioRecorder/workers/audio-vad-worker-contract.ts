import { RpcNoWait, RpcTimeout } from 'rpc';
import { AudioDiagnosticsState } from '../audio-recorder';
import type { AppConstants } from 'app-constants';
import type { SharedSettingsSnapshot } from 'shared-settings';
import type { SharedSettingsWorker } from 'shared-settings-worker';

export interface AudioVadWorker extends SharedSettingsWorker {
    create(appConstants: AppConstants, artifactVersions: Map<string, string>, sharedSettings: SharedSettingsSnapshot, canUseNNVad: boolean, timeout?: RpcTimeout): Promise<void>;
    init(workletPort: MessagePort, encoderWorkerPort: MessagePort): Promise<void>;
    reset(): Promise<void>;
    conversationSignal(noWait?: RpcNoWait): Promise<void>;
    runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState>;

    onFrame(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}
