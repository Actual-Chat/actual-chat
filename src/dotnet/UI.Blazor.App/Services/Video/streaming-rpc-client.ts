// Streaming RPC client — thin UI-side wrapper over the shared Api + streamingApi.

import { RpcStream } from 'actuallab-rpc';
import { Api, MediaRpcStreamOptions, type VideoFrameDto, type AudioFrameDto } from 'api';

/**
 * Create a client-side RPC stream for pushing video frames to the server.
 * Real-time mode: uses the shared video RPC stream policy, but disables reconnect.
 *
 * Usage: pass `source` (an AsyncIterable of frames), then call `stream.toRef(peer)`
 * to get the ref for the RPC method argument. `toRef` registers the sender and
 * starts pumping automatically.
 */
export function createVideoStream(source: AsyncIterable<VideoFrameDto>): { stream: RpcStream<VideoFrameDto>; ref: unknown } {
    const peer = Api.peer;
    const stream = new RpcStream<VideoFrameDto>(
        source,
        {
            ...MediaRpcStreamOptions.videoRealtime<VideoFrameDto>(),
            allowReconnect: false,
        },
    );
    return { stream, ref: stream.toRef(peer) };
}

/**
 * Create a client-side RPC stream for pushing audio frames to the server.
 * Non-real-time recording policy.
 */
export function createAudioStream(source: AsyncIterable<AudioFrameDto>): { stream: RpcStream<AudioFrameDto>; ref: unknown } {
    const peer = Api.peer;
    const stream = new RpcStream<AudioFrameDto>(source, MediaRpcStreamOptions.audioRecording<AudioFrameDto>());
    return { stream, ref: stream.toRef(peer) };
}
