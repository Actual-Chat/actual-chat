// Fusion RPC service definitions for video streaming.
// These match the .NET ILiveVideoStreams and IStreamServer contracts.

import { defineRpcService, RpcType } from 'actuallab-rpc';

// --- ILiveVideoStreams (video pull) ---
// .NET: Task<RpcStream<VideoFrame>?> GetStream(Session session, StreamId streamId, TimeSpan skipTo, CancellationToken ct)
// Wire: "ILiveVideoStreams.GetStream:3" (3 args excl. CancellationToken)
export const LiveVideoStreamsDef = defineRpcService('ILiveVideoStreams', {
    // wireArgCount = args.length + 1 (CancellationToken) by default
    GetStream: { args: ['session', 'streamId', 'skipTo'], returns: RpcType.stream },
    List: { args: ['session', 'chatId'] },
    GetMemberCount: { args: ['session', 'chatId'] },
    GetSupportedCodecs: { args: ['session', 'chatId'] },
    GetQualityPreset: { args: ['session', 'streamId'] },
    RegisterMember: { args: ['session', 'chatId', 'supportedDecoderCodecs'] },
    UnregisterMember: { args: ['session', 'chatId'] },
});

// --- IStreamServer (video push + audio) ---
// .NET: Task PushVideo(Session session, string chatId, double clientStartOffset, VideoFormat format, RpcStream<VideoFrame> frameStream, CancellationToken ct)
export const StreamServerDef = defineRpcService('IStreamServer', {
    PushVideo: { args: ['session', 'chatId', 'clientStartOffset', 'format', 'frameStream'] },
});

// --- VideoFrame TypeScript interface ---
// Matches .NET VideoFrame serialized via MessagePack with implicit string keys.
// VideoFrame.cs is [MessagePackObject(true)] (no explicit [Key] attrs), so the
// wire keys are PascalCase property names.
// TimeSpan is serialized as int64 ticks (100ns units).
export interface VideoFrameDto {
  Data: Uint8Array;
  Offset: number;       // TimeSpan ticks (int64)
  Duration: number;     // TimeSpan ticks (int64)
  IsKeyFrame: boolean;
  Width?: number;
  Height?: number;
  Description?: Uint8Array | null;
  Codec?: string | null;
  TemporalLayerId?: number;
}

// --- VideoFormat TypeScript interface ---
// Matches .NET VideoFormat serialized via MessagePack with implicit string keys.
// VideoFormat.cs is [MessagePackObject(true)] → PascalCase property names.
export interface VideoFormatDto {
  Codec: string;
  Width: number;
  Height: number;
  CodecSettings: string;
}
