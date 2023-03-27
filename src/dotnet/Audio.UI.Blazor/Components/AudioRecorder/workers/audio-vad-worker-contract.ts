import { RpcNoWait, RpcTimeout } from 'rpc';

export interface AudioVadWorker {
    create(artifactVersions: Map<string, string>, timeout?: RpcTimeout): Promise<void>;
    init(workletPort: MessagePort, encoderWorkerPort: MessagePort): Promise<void>;
    reset(): Promise<void>;

    onFrame(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}
