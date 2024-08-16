import { RpcNoWait, RpcTimeout } from 'rpc';
import { AudioDiagnosticsState } from "../audio-recorder";

export interface AudioVadWorker {
    create(artifactVersions: Map<string, string>, canUseNNVad: boolean, timeout?: RpcTimeout): Promise<void>;
    init(workletPort: MessagePort, encoderWorkerPort: MessagePort): Promise<void>;
    reset(): Promise<void>;
    conversationSignal(noWait?: RpcNoWait): Promise<void>;
    runDiagnostics(diagnosticsState: AudioDiagnosticsState): Promise<AudioDiagnosticsState>;

    onFrame(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}
