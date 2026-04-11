// Video RPC client — connects to ILiveVideoStreams and IStreamServer via Fusion RPC WebSocket.
// Replaces SignalR for both video pull (GetStream) and push (PushVideo) paths.

import { RpcHub, RpcClientPeer, RpcClientStreamSender } from 'actuallab-rpc';
import { LiveVideoStreamsDef, StreamServerDef, type VideoFrameDto, type VideoFormatDto } from './video-rpc-service.js';

let _hub: RpcHub | undefined;
let _peer: RpcClientPeer | undefined;
let _pullClient: VideoRpcPullClient | undefined;
let _pushClient: VideoRpcPushClient | undefined;
let _baseUrl: string | undefined;

/** Serialization format — binary MessagePack for efficient video frame transport. */
const SERIALIZATION_FORMAT = 'msgpack6';

// --- Pull client (ILiveVideoStreams) ---

export interface VideoRpcPullClient {
  GetStream(session: string, streamId: string, skipToTicks: number): Promise<AsyncIterable<VideoFrameDto>>;
  List(session: string, chatId: string): Promise<unknown>;
}

// --- Push client (IStreamServer) ---

export interface VideoRpcPushClient {
  PushVideo(session: string, chatId: string, clientStartOffset: number,
    format: VideoFormatDto, continuationOf: string | null, frameStreamRef: unknown): Promise<void>;
}

/**
 * Initialize the video RPC client.
 * @param rpcWsUrl Full WebSocket URL for the RPC endpoint, e.g. "wss://local.voxt.ai/rpc/ws"
 */
export function initVideoRpc(rpcWsUrl: string): void {
    _baseUrl = rpcWsUrl;
}

function ensurePeer(): RpcClientPeer {
    if (!_peer) {
        if (!_baseUrl)
            throw new Error('Video RPC not initialized. Call initVideoRpc(url) first.');
        _hub = new RpcHub(); // hubId must be a UUID for binary MessagePack GuidFormatter
        _peer = new RpcClientPeer(_hub, _baseUrl, SERIALIZATION_FORMAT);
        void _peer.run();
    }
    return _peer;
}

/** Get the ILiveVideoStreams RPC client (video pull). */
export function getVideoRpcClient(): VideoRpcPullClient {
    if (!_pullClient) {
        const peer = ensurePeer();
        _pullClient = _hub!.addClient(peer, LiveVideoStreamsDef) as unknown as VideoRpcPullClient;
    }
    return _pullClient;
}

/** Get the IStreamServer RPC client (video push). */
export function getVideoRpcPushClient(): VideoRpcPushClient {
    if (!_pushClient) {
        const peer = ensurePeer();
        _pushClient = _hub!.addClient(peer, StreamServerDef) as unknown as VideoRpcPushClient;
    }
    return _pushClient;
}

/**
 * Create a client-side stream sender for pushing video frames to the server.
 * Returns the sender (call writeFrom/sendItem) and its ref object (pass as RPC
 * method argument — the binary serializer encodes it as a MessagePack map).
 */
export function createVideoFrameSender(): { sender: RpcClientStreamSender<VideoFrameDto>; ref: unknown } {
    const peer = ensurePeer();
    const sender = new RpcClientStreamSender<VideoFrameDto>(peer);
    return { sender, ref: sender.toRef() };
}

/** Disconnect the video RPC client. */
export function disconnectVideoRpc(): void {
    _peer?.close();
    _peer = undefined;
    _pullClient = undefined;
    _pushClient = undefined;
    _hub = undefined;
}
