import { RpcNoWait } from 'rpc';
import { AudioDiagnosticsState } from "../audio-recorder";

export interface AudioVadWorklet {
    init(workerPort: MessagePort, noWait?: RpcNoWait): Promise<void>;
    start(windowSizeMs: 30 | 32): Promise<void>;
    releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
    terminate(noWait?: RpcNoWait): Promise<void>;
    runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState>;
}
