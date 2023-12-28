import { VideoEncoderWorker } from "./video-encoder-worker-contract";
import { RpcNoWait, RpcTimeout } from "rpc";

let hubConnection: signalR.HubConnection;
let recordingSubject: signalR.Subject<Array<Uint8Array>> = null;
let state: 'inactive' | 'created' | 'encoding' | 'ended' = 'inactive';
let _sessionToken = '';
let _сhatId = '';
let _canvas: OffscreenCanvas | null = null;
let _framerate: number;
let _encoder: VideoEncoder | null = null;

const serverImpl: VideoEncoderWorker = {
    create: (artifactVersions: Map<string, string>, streamHubUrl: string, timeout?: RpcTimeout): Promise<void> => Promise.resolve(undefined),

    disconnect: (noWait?: RpcNoWait): Promise<void> => Promise.resolve(undefined),

    reconfigure: (framerate: number, bitrate: number): Promise<void> => Promise.resolve(undefined),

    reconnect: (noWait?: RpcNoWait): Promise<void> => Promise.resolve(undefined),

    setSessionToken: async (sessionToken: string, noWait?: RpcNoWait): Promise<void> => {
        _sessionToken = sessionToken;
    },

    start: async (chatId: string, offscreenCanvas: OffscreenCanvas, framerate: number, bitrate: number): Promise<void> => {
        _сhatId = chatId;
        _canvas = offscreenCanvas;
        _framerate = framerate;
        const config = await getEncoderConfig(framerate, bitrate, offscreenCanvas.width, offscreenCanvas.height);
        _encoder = new VideoEncoder({
            output: onEncodedChunk,
            error: onError
        });

    },

    stop: (): Promise<void> => Promise.resolve(undefined),

};

function onEncodedChunk(chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata): void {
    console.log('onEncodedChunk', chunk, metadata);
}

function onError(error: DOMException): void {
    console.error('onError', error);
}

async function getEncoderConfig(framerate: number, bitrate: number, width: number, height: number): Promise<VideoEncoderConfig> {
    let coderConfig = {
        codec: 'vp09.00.10.08',
        height: height,
        width: width,
        bitrate: bitrate,
        bitrateMode: 'variable',
        framerate: framerate,
        hardwareAcceleration: 'prefer-hardware',
    } as VideoEncoderConfig;
    let encoderSupported = await VideoEncoder.isConfigSupported(coderConfig);
    if (encoderSupported.supported)
        return encoderSupported.config;

    coderConfig.hardwareAcceleration = undefined;
    encoderSupported = await VideoEncoder.isConfigSupported(coderConfig);
    if (encoderSupported.supported)
        return encoderSupported.config;

    coderConfig.codec = 'vp8';
    coderConfig.hardwareAcceleration = 'prefer-hardware';
    encoderSupported = await VideoEncoder.isConfigSupported(coderConfig);
    if (encoderSupported.supported)
        return encoderSupported.config;

    coderConfig.hardwareAcceleration = undefined;
    encoderSupported = await VideoEncoder.isConfigSupported(coderConfig);
    if (encoderSupported.supported)
        return encoderSupported.config;

    throw new Error('VP8 and VP9 codecs are not supported');
}
