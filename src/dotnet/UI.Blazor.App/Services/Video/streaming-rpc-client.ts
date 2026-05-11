// Streaming RPC client — thin UI-side wrapper over Api + streamingApi.

import { RpcStream } from 'actuallab-rpc';
import { Api, MediaRpcStreamOptions, type VideoFrameDto, type AudioFrameDto } from 'api';

// Real-time video: shared video stream policy with reconnect disabled.
// Keyframe predicate: a frame is a keyframe iff `KeyFrameIndex === Index`.
export function createVideoStream(source: AsyncIterable<VideoFrameDto>): { stream: RpcStream<VideoFrameDto>; ref: unknown } {
    const peer = Api.peer;
    const stream = new RpcStream<VideoFrameDto>(
        source,
        {
            ...MediaRpcStreamOptions.videoRealtime<VideoFrameDto>(
                item => item.KeyFrameIndex !== undefined && item.KeyFrameIndex === item.Index),
            allowReconnect: false,
        },
    );
    return { stream, ref: stream.toRef(peer) };
}

// Non-real-time audio recording policy.
export function createAudioStream(source: AsyncIterable<AudioFrameDto>): { stream: RpcStream<AudioFrameDto>; ref: unknown } {
    const peer = Api.peer;
    const stream = new RpcStream<AudioFrameDto>(source, MediaRpcStreamOptions.audioRecording<AudioFrameDto>());
    return { stream, ref: stream.toRef(peer) };
}
