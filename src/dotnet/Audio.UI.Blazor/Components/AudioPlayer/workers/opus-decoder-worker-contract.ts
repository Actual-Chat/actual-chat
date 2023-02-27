import { RpcNoWait } from 'rpc';

export interface OpusDecoderWorker {
    init(artifactVersions: Map<string, string>): Promise<void>;
    create(streamId: string, workletMessagePort: MessagePort): Promise<void>;
    stop(streamId: string): Promise<void>;
    end(streamId: string): Promise<void>;
    close(streamId: string, noWait?: RpcNoWait): Promise<void>;

    // ArrayBuffer can be bigger than Uint8Array and can the latter can be started with offset within the buffer
    // so we should transfer offset and length too
    onFrame(streamId: string, buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void>;
}
