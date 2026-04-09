// Video RPC client — connects to ILiveVideoStreams via Fusion RPC WebSocket.
// Replaces SignalR's GetVideo path for video frame pull.

import { RpcHub, RpcClientPeer } from 'actuallab-rpc';
import { LiveVideoStreamsDef, type VideoFrameDto } from './video-rpc-service.js';

let _hub: RpcHub | undefined;
let _peer: RpcClientPeer | undefined;
let _client: VideoRpcClient | undefined;
let _baseUrl: string | undefined;

/** Serialization format — use binary MessagePack for efficient video frame transport. */
const SERIALIZATION_FORMAT = 'msgpack6np';

export interface VideoRpcClient {
  GetStream(session: string, streamId: string, skipToTicks: number): Promise<AsyncIterable<VideoFrameDto>>;
  List(session: string, chatId: string): Promise<unknown>;
}

/**
 * Initialize the video RPC client.
 * @param rpcWsUrl Full WebSocket URL for the RPC endpoint, e.g. "wss://local.voxt.ai/rpc/ws"
 */
export function initVideoRpc(rpcWsUrl: string): void {
  _baseUrl = rpcWsUrl;
}

/** Get the ILiveVideoStreams RPC client. Lazily creates hub + peer + starts connection. */
export function getVideoRpcClient(): VideoRpcClient {
  if (!_client) {
    if (!_baseUrl)
      throw new Error('Video RPC not initialized. Call initVideoRpc(url) first.');

    _hub = new RpcHub('video-rpc');
    _peer = new RpcClientPeer(_hub, _baseUrl, SERIALIZATION_FORMAT);
    _client = _hub.addClient(_peer, LiveVideoStreamsDef) as unknown as VideoRpcClient;
    // Start connection loop (reconnects automatically)
    void _peer.run();
  }
  return _client;
}

/** Disconnect the video RPC client. */
export function disconnectVideoRpc(): void {
  _peer?.close();
  _peer = undefined;
  _client = undefined;
  _hub = undefined;
}
