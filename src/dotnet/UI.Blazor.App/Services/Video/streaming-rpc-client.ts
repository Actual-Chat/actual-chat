// Streaming RPC client — connects to IStreamServer via Fusion RPC WebSocket.

import { RpcHub, RpcClientPeer, RpcStream } from 'actuallab-rpc';
import { StreamServerDef, type VideoFrameDto, type VideoFormatDto, type AudioFrameDto } from './streaming-rpc-service.js';

let _hub: RpcHub | undefined;
let _peer: RpcClientPeer | undefined;
let _streamServerClient: StreamServerClient | undefined;
let _baseUrl: string | undefined;

/** Serialization format — binary MessagePack for efficient video frame transport. */
const SERIALIZATION_FORMAT = 'msgpack6';

// --- IStreamServer (stream push/pull + control) ---

export interface StreamServerClient {
    GetVideo(streamId: string, skipToTicks: number): Promise<AsyncIterable<VideoFrameDto>>;
    PushVideo(
        session: string,
        chatId: string,
        clientStartOffset: number,
        format: VideoFormatDto,
        frameStreamRef: unknown): Promise<void>;
    PushAudio(
        session: string,
        chatId: string,
        repliedChatEntryId: string | null,
        clientStartOffset: number,
        preSkip: number,
        frameStreamRef: unknown): Promise<void>;
    RequestKeyFrame(streamId: string): Promise<void>;
    ReportVideoLatency(
        streamId: string,
        streamOffsetMs: number,
        medianDecodeTimeMs: number,
        bufferDepth: number,
        bufferSpanMs: number): Promise<number>;
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

/** Get the IStreamServer RPC client (stream push/pull + control). */
export function getStreamServerClient(): StreamServerClient {
    if (!_streamServerClient) {
        const peer = ensurePeer();
        _streamServerClient = _hub!.addClient(peer, StreamServerDef) as unknown as StreamServerClient;
    }
    return _streamServerClient;
}

/**
 * Create a client-side RPC stream for pushing video frames to the server.
 * Real-time mode: isRealTime=true, allowReconnect=false, ackPeriod=5, ackAdvance=31.
 *
 * Usage: pass `source` (an AsyncIterable of frames), then call `stream.toRef(peer)`
 * to get the ref for the RPC method argument. `toRef` registers the sender and
 * starts pumping automatically.
 */
export function createVideoStream(source: AsyncIterable<VideoFrameDto>): { stream: RpcStream<VideoFrameDto>; ref: unknown } {
    const peer = ensurePeer();
    const stream = new RpcStream<VideoFrameDto>(source, {
        isRealTime: true, allowReconnect: false, ackPeriod: 5, ackAdvance: 31,
        canSkipTo: (frame) => frame.IsKeyFrame,
    });
    return { stream, ref: stream.toRef(peer) };
}

/**
 * Create a client-side RPC stream for pushing audio frames to the server.
 * Non-real-time with default parameters.
 */
export function createAudioStream(source: AsyncIterable<AudioFrameDto>): { stream: RpcStream<AudioFrameDto>; ref: unknown } {
    const peer = ensurePeer();
    const stream = new RpcStream<AudioFrameDto>(source);
    return { stream, ref: stream.toRef(peer) };
}

/** Disconnect the RPC client. */
export function disconnectVideoRpc(): void {
    _peer?.close();
    _peer = undefined;
    _streamServerClient = undefined;
    _hub = undefined;
}
