// Streaming RPC client — thin UI-side wrapper over the shared Api + streamingApi.

import { RpcStream } from 'actuallab-rpc';
import { Api, type VideoFrameDto, type AudioFrameDto } from 'api';

/**
 * Create a client-side RPC stream for pushing video frames to the server.
 * Real-time mode: isRealTime=true, allowReconnect=false, ackPeriod=5, bufferSize=31.
 *
 * Usage: pass `source` (an AsyncIterable of frames), then call `stream.toRef(peer)`
 * to get the ref for the RPC method argument. `toRef` registers the sender and
 * starts pumping automatically.
 */
export function createVideoStream(source: AsyncIterable<VideoFrameDto>): { stream: RpcStream<VideoFrameDto>; ref: unknown } {
    const peer = Api.peer;
    const stream = new RpcStream<VideoFrameDto>(source, {
        isRealTime: true, allowReconnect: false, ackPeriod: 5, bufferSize: 31,
        canSkipTo: (frame) => frame.IsKeyFrame,
    });
    return { stream, ref: stream.toRef(peer) };
}

/**
 * Create a client-side RPC stream for pushing audio frames to the server.
 * Non-real-time with default parameters.
 */
export function createAudioStream(source: AsyncIterable<AudioFrameDto>): { stream: RpcStream<AudioFrameDto>; ref: unknown } {
    const peer = Api.peer;
    const stream = new RpcStream<AudioFrameDto>(source);
    return { stream, ref: stream.toRef(peer) };
}
