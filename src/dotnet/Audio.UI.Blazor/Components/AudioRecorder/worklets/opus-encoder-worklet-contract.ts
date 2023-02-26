import { RpcNoWait } from 'rpc';

export interface OpusEncoderWorklet {
    init(workerPort: MessagePort): Promise<void>;

    onSample(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}
