// Fusion RPC service definitions for video streaming.
// These match the .NET ILiveVideoStreams and IStreamServer contracts.

import { defineRpcService, RpcType } from 'actuallab-rpc';

// --- ILiveVideoStreams (video pull) ---
// .NET: Task<RpcStream<VideoFrame>?> GetStream(Session session, StreamId streamId, TimeSpan skipTo, CancellationToken ct)
// Wire: "ILiveVideoStreams.GetStream:3" (3 args excl. CancellationToken)
export const LiveVideoStreamsDef = defineRpcService("ILiveVideoStreams", {
  // wireArgCount = args.length + 1 (CancellationToken) by default
  GetStream: { args: ["session", "streamId", "skipTo"], returns: RpcType.stream },
  List: { args: ["session", "chatId"] },
  GetMemberCount: { args: ["session", "chatId"] },
  GetSupportedCodecs: { args: ["session", "chatId"] },
  GetQualityPreset: { args: ["session", "streamId"] },
  RegisterMember: { args: ["session", "chatId", "supportedDecoderCodecs"] },
  UnregisterMember: { args: ["session", "chatId"] },
});

// --- IStreamServer (video push + audio) ---
// .NET: Task PushVideo(Session session, string chatId, double clientStartOffset, VideoFormat format, RpcStream<VideoFrame> frameStream, CancellationToken ct)
export const StreamServerDef = defineRpcService("IStreamServer", {
  PushVideo: { args: ["session", "chatId", "clientStartOffset", "format", "frameStream"] },
});

// --- VideoFrame TypeScript interface ---
// Matches .NET VideoFrame serialized via MessagePack with string keys.
// TimeSpan is serialized as int64 ticks (100ns units).
export interface VideoFrameDto {
  data: Uint8Array;
  offset: number;       // TimeSpan ticks (int64)
  duration: number;     // TimeSpan ticks (int64)
  isKeyFrame: boolean;
  width?: number;
  height?: number;
  description?: Uint8Array | null;
  codec?: string | null;
  temporalLayerId?: number;
}
