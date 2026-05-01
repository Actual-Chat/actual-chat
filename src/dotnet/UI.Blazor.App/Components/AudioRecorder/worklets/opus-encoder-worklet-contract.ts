import { RpcNoWait } from 'rpc';
import { AudioDiagnosticsState } from '../audio-recorder';
import type { AppConstants } from 'app-constants';

export interface OpusEncoderWorklet {
    init(appConstants: AppConstants, workerPort: MessagePort): Promise<void>;
    start(noWait?: RpcNoWait): Promise<void>;
    releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
    terminate(noWait?: RpcNoWait): Promise<void>;
    runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState>;
}
