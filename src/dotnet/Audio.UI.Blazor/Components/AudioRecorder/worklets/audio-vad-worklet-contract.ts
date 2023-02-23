import { RpcNoWait } from 'rpc';

export interface AudioVadWorklet {
    init(workerPort: MessagePort, noWait?: RpcNoWait): Promise<void>;
    onSample(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}
