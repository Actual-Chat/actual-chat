import { RpcNoWait } from 'rpc';

export interface OpusDecoderWorker {
    create(artifactVersions: Map<string, string>): Promise<void>;
    start(controllerId: number, workletMessagePort: MessagePort): Promise<void>;
    stop(controllerId: number): Promise<void>;
    end(controllerId: number): Promise<void>;
    disposeDecoder(controllerId: number): Promise<void>;

    // ArrayBuffer can be bigger than Uint8Array and can the latter can be started with offset within the buffer
    // so we should transfer offset and length too
    onEncodedChunk(controllerId: number, buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void>;
}
