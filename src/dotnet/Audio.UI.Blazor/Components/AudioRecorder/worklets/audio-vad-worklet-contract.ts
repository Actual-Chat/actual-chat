import { RpcNoWait } from 'rpc';

export interface AudioVadWorklet {
    init(workerPort: MessagePort, noWait?: RpcNoWait): Promise<void>;
    releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
    stop(noWait?: RpcNoWait): Promise<void>;
}
