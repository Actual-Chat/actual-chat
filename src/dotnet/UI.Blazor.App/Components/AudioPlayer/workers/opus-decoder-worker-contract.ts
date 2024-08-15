import { RpcNoWait, RpcTimeout } from 'rpc';

export interface OpusDecoderWorker {
    create(artifactVersions: Map<string, string>, timeout?: RpcTimeout): Promise<void>;
    init(streamId: string, feederWorkletPort: MessagePort): Promise<void>;
    resume(streamId: string, noWait?: RpcNoWait): Promise<void>;
    frame(streamId: string, buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void>;
    end(streamId: string, mustAbort: boolean): Promise<void>;
    close(streamId: string, noWait?: RpcNoWait): Promise<void>;
    releaseBuffer(streamId: string, buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}

export interface BufferHandler {
    releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}
