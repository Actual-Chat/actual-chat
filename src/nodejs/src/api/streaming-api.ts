// Streaming RPC module — service contract (IStreamServer), DTO types,
// the `StreamingApi` module class, and a typed `streamingApi.streamServer`
// accessor on its singleton instance. A module file conventionally colocates
// its types, service defs, and registration.
//
// Usage:
//     Api.init('Example', { url, modules: [streamingApi] });
//     await streamingApi.streamServer.PushVideo(...);

import { defineRpcService, RpcRemoteExecutionMode, RpcType, type RpcHub } from 'actuallab-rpc';
import { Api, type ApiModule } from './api.js';
import type { Moment } from './rpc-scalars.js';
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
    ReportVideoLatency: { args: ['streamId', 'report'] },
});

// --- VideoFrame TypeScript interface ---
// Matches .NET VideoFrame serialized via MessagePack with implicit string keys.
// VideoFrame.cs is [MessagePackObject(true)] (no explicit [Key] attrs), so the
// wire keys are PascalCase property names.
// TimeSpan is serialized as int64 ticks (100ns units).
export interface VideoFrameDto {
    Data: Uint8Array;
    Offset: Moment;       // TimeSpan ticks (int64)
    Duration: Moment;     // TimeSpan ticks (int64)
    IsKeyFrame: boolean;
    Width?: number;
    Height?: number;
    Description?: Uint8Array | null;
    Codec?: string | null;
    TemporalLayerId?: number;
    // SVC spatial layer ID. 0 = base (lowest-res) layer, 1+ = higher-res simulcast
    // layers. Always 0 on single-encoder (P2P) streams. Maps to .NET VideoFrame.SpatialLayerId (int).
    SpatialLayerId?: number;
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

// --- VideoLatencyReport TypeScript interface ---
// Matches .NET VideoLatencyReport serialized via MessagePack with implicit
// string keys — [MessagePackObject(true)] → PascalCase wire keys.
// All metric fields default to sentinel values server-side (-1 / null) when
// absent, so clients can omit fields they haven't measured this tick.
export interface VideoLatencyReportDto {
    StreamOffsetMs: number;
    // -1 = not measured this tick.
    MedianDecodeTimeMs?: number;
    // -1 = not measured.
    BufferDepth?: number;
    // -1 = not measured.
    BufferSpanMs?: number;
    // null = no render-size hint; numeric = VideoQualityLevel ordinal.
    // Server maps non-null via StreamLatencyStore.MapRenderLevelToSpatialLayer.
    RenderQuality?: number | null;
    // document.visibilityState === 'visible'. Defaults to true server-side.
    IsVisible?: boolean;
}

// --- VideoLatencyReportResponse TypeScript interface ---
// Matches .NET VideoLatencyReportResponse via MessagePack [MessagePackObject(true)].
// Returned from ReportVideoLatency; carries the SFU's currently-forwarded spatial
// layer + its coded WxH for this peer, used by the diagnostics modal.
export interface VideoLatencyReportResponseDto {
    // -1 = no frame yet forwarded to this peer.
    ForwardedSpatialLayerId: number;
    // 0 = unknown (no frame seen yet).
    ForwardedWidth: number;
    ForwardedHeight: number;
    // Highest layer the producer is currently emitting (for debugging).
    ObservedMaxSpatialLayer: number;
}

// --- AudioFrame TypeScript interface ---
// Matches .NET AudioFrame serialized via MessagePack with implicit string keys.
// AudioFrame.cs is [MessagePackObject(true)] → PascalCase property names.
// TimeSpan is serialized as int64 ticks (100ns units).
export interface AudioFrameDto {
    Data: Uint8Array;
    Offset: Moment;       // TimeSpan ticks (int64)
    Duration: Moment;     // TimeSpan ticks (int64)
    IsKeyFrame: boolean;  // always true for audio
}

// --- Typed proxy for IStreamServer calls on the client side. ---
export interface StreamServerClient {
    GetVideo(streamId: string, skipToTicks: Moment): Promise<AsyncIterable<VideoFrameDto>>;
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
    ReportVideoLatency(streamId: string, report: VideoLatencyReportDto): Promise<VideoLatencyReportResponseDto>;
}

// Mirrors .NET VideoQualityLevel enum. Lower numeric value = higher quality.
// Used as the `RenderQuality` field on VideoLatencyReportDto — pick the
// smallest level whose nominal dims meet or approximately match the
// consumer's actual render size. Server maps Low/Medium→spatial 1,
// High→2, Full/Ultra→uncapped (producer's observedMaxSpatial decides).
// Use `null` for "not hinted" (server applies no render cap); using a
// number forces server-side interpretation of that level.
export const VideoQualityLevelUltra = 0;
export const VideoQualityLevelFull = 1;
export const VideoQualityLevelHigh = 2;
export const VideoQualityLevelMedium = 3;
export const VideoQualityLevelLow = 4;

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
