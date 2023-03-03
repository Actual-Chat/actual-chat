import { RpcNoWait } from 'rpc';

export interface OpusEncoderWorklet {
    init(workerPort: MessagePort): Promise<void>;
    releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}
