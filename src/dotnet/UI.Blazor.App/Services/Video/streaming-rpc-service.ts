// Fusion RPC service definitions for streaming.
// Matches the .NET IStreamServer contract.

import { defineRpcService, RpcType } from 'actuallab-rpc';

// --- IStreamServer (stream push/pull + control) ---
export const StreamServerDef = defineRpcService('IStreamServer', {
    GetVideo: { args: ['streamId', 'skipTo'], returns: RpcType.stream },
    PushVideo: { args: ['session', 'chatId', 'clientStartOffset', 'format', 'frameStream'] },
    PushAudio: { args: ['session', 'chatId', 'repliedChatEntryId', 'clientStartOffset', 'preSkip', 'frameStream'] },
    RequestKeyFrame: { args: ['streamId'] },
    ReportVideoLatency: { args: ['streamId', 'streamOffsetMs', 'medianDecodeTimeMs', 'bufferDepth', 'bufferSpanMs'] },
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

// --- AudioFrame TypeScript interface ---
// Matches .NET AudioFrame serialized via MessagePack with implicit string keys.
// AudioFrame.cs is [MessagePackObject(true)] → PascalCase property names.
// TimeSpan is serialized as int64 ticks (100ns units).
export interface AudioFrameDto {
    Data: Uint8Array;
    Offset: number;       // TimeSpan ticks (int64)
    Duration: number;     // TimeSpan ticks (int64)
    IsKeyFrame: boolean;  // always true for audio
}
