import { RpcNoWait } from 'rpc';

export interface OpusEncoderWorklet {
    init(workerPort: MessagePort): Promise<void>;
    releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
    stop(noWait?: RpcNoWait): Promise<void>;
}
