import { RpcNoWait } from 'rpc';

export interface OpusEncoderWorklet {
    init(workerPort: MessagePort): Promise<void>;

    onFrame(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}
