import { RpcNoWait } from 'rpc';
import { AudioDiagnosticsState } from '../audio-recorder';
import type { AppConstants } from 'app-constants';

export interface AudioVadWorklet {
    init(appConstants: AppConstants, workerPort: MessagePort, noWait?: RpcNoWait): Promise<void>;
    start(windowSizeMs: 30 | 32): Promise<void>;
    releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
    terminate(noWait?: RpcNoWait): Promise<void>;
    runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState>;
}
