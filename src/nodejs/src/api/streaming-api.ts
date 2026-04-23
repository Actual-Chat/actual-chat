// Streaming RPC module — service contract (IStreamServer), DTO types,
// the `StreamingApi` module class, and a typed `streamingApi.streamServer`
// accessor on its singleton instance. A module file conventionally colocates
// its types, service defs, and registration.
//
// Usage:
//     Api.init(url, streamingApi);
//     await streamingApi.streamServer.PushVideo(...);

import { defineRpcService, RpcRemoteExecutionMode, RpcType, type RpcHub } from 'actuallab-rpc';
import { Api, type ApiModule } from './api.js';
import { coreApi } from './core-api.js';

// Streaming push calls: fire-and-forget.  AwaitForConnection lets us wait for the WS to
// come up before initial send; AllowReconnect makes the $sys.Reconnect protocol skip the
// call on same-peer reconnect (server still has the handler, stream resumes via ACK).
// We deliberately DO NOT set AllowResend: on peer change the call + stream fail, and
// the caller recreates them.  Mirror of [RpcMethod] on IStreamServer in .NET.
const StreamPushMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect;

// --- IStreamServer (stream push/pull + control) ---
export const StreamServerDef = defineRpcService('IStreamServer', {
    GetVideo: { args: ['streamId', 'skipTo'], returns: RpcType.stream },
    PushVideo: { args: ['session', 'chatId', 'clientStartOffset', 'format', 'frameStream', 'streamKind'], remoteExecutionMode: StreamPushMode },
    PushAudio: { args: ['session', 'chatId', 'repliedChatEntryId', 'clientStartOffset', 'preSkip', 'frameStream'], remoteExecutionMode: StreamPushMode },
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
    // Native source dimensions, keyframe only. Lets server track source-resolution
    // growth (e.g. screencast window resize) and unlock higher quality tiers mid-stream.
    SourceWidth?: number;
    SourceHeight?: number;
}

// --- VideoFormat TypeScript interface ---
// Matches .NET VideoFormat serialized via MessagePack with implicit string keys.
// VideoFormat.cs is [MessagePackObject(true)] → PascalCase property names.
export interface VideoFormatDto {
    Codec: string;
    Width: number;
    Height: number;
    CodecSettings: string;
    SourceWidth: number;
    SourceHeight: number;
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

// --- Typed proxy for IStreamServer calls on the client side. ---
export interface StreamServerClient {
    GetVideo(streamId: string, skipToTicks: number): Promise<AsyncIterable<VideoFrameDto>>;
    PushVideo(
        session: string,
        chatId: string,
        clientStartOffset: number,
        format: VideoFormatDto,
        frameStreamRef: unknown,
        streamKind: number): Promise<void>;
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

/** Streaming module — pass the `streamingApi` singleton (below) to `Api.init`
 *  and reach typed services through it, e.g. `streamingApi.streamServer.PushVideo(...)`.
 *  The class is intentionally not exported; use `typeof streamingApi` if you
 *  need the type. */
class StreamingApi implements ApiModule {
    readonly deps = [coreApi];
    register(hub: RpcHub): void {
        // Pre-populate the method registry so compact-format hash resolution
        // works from the very first outbound message. (`hub.addClient` will
        // also register, but lazy — this keeps startup deterministic.)
        hub.registry.registerService(StreamServerDef.name, StreamServerDef.methods);
    }

    private _streamServer: StreamServerClient | undefined;
    /** Typed `IStreamServer` client bound to the shared default peer. Lazy —
     *  created on first access, then cached for the lifetime of the module. */
    get streamServer(): StreamServerClient {
        return this._streamServer
            ??= Api.hub.addClient<StreamServerClient>(Api.peer, StreamServerDef);
    }
}

export const streamingApi = new StreamingApi();
