import { RpcNoWait } from 'rpc';

export interface AudioVadWorklet {
    init(workerPort: MessagePort, noWait?: RpcNoWait): Promise<void>;
    onFrame(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}
