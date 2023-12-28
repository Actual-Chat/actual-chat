import { RpcNoWait, RpcTimeout } from 'rpc';

export interface VideoEncoderWorker {
    create(artifactVersions: Map<string, string>, streamHubUrl: string, timeout?: RpcTimeout): Promise<void>;
    setSessionToken(sessionToken: string, noWait?: RpcNoWait): Promise<void>;
    start(chatId: string, offscreenCanvas: OffscreenCanvas, framerate: number, bitrate: number): Promise<void>;
    reconfigure(framerate: number, bitrate: number): Promise<void>;
    stop(): Promise<void>;

    reconnect(noWait?: RpcNoWait): Promise<void>;
    disconnect(noWait?: RpcNoWait): Promise<void>;
}
