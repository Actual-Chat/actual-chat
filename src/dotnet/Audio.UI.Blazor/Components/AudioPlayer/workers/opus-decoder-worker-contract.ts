import { RpcNoWait } from 'rpc';

export interface OpusDecoderWorker {
    init(artifactVersions: Map<string, string>): Promise<void>;
    create(streamId: string, workletMessagePort: MessagePort): Promise<void>;
    frame(streamId: string, buffer: ArrayBuffer, offset: number, length: number, noWait?: RpcNoWait): Promise<void>;
    end(streamId: string, mustAbort: boolean): Promise<void>;
    close(streamId: string, noWait?: RpcNoWait): Promise<void>;
}
