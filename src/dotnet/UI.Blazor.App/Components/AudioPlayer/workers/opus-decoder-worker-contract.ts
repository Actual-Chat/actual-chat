import { RpcNoWait, RpcTimeout } from 'rpc';
import type { AppConstants } from 'app-constants';
import type { SharedSettingsSnapshot } from 'shared-settings';
import type { SharedSettingsWorker } from 'shared-settings-worker';

export interface OpusDecoderWorker extends SharedSettingsWorker {
    create(appConstants: AppConstants, artifactVersions: Map<string, string>, sharedSettings: SharedSettingsSnapshot, timeout?: RpcTimeout): Promise<void>;
    init(streamId: string, feederWorkletPort: MessagePort): Promise<void>;
    resume(streamId: string, sourceRecordedAtMs: number, noWait?: RpcNoWait): Promise<void>;
    setTargetBufferSize(streamId: string, targetBufferSizeMs: number, noWait?: RpcNoWait): Promise<void>;
    frame(
        streamId: string,
        buffer: ArrayBuffer,
        offset: number,
        length: number,
        sourceOffsetMs: number,
        noWait?: RpcNoWait): Promise<void>;
    end(streamId: string, mustAbort: boolean): Promise<void>;
    close(streamId: string, noWait?: RpcNoWait): Promise<void>;
    releaseBuffer(streamId: string, buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}

export interface BufferHandler {
    requestFrame(noWait?: RpcNoWait): Promise<void>;
    releaseBuffer(buffer: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
}
