// Streaming RPC module — service contracts (ILiveAudioStreams, ILiveVideoStreams,
// IStreamServer for v2.6 compat), DTO types, the `StreamingApi` module class,
// and typed `streamingApi.{liveAudioStreams,liveVideoStreams,streamServer}`
// accessors on its singleton instance.
//
// Usage:
//     Api.init('Example', { url, modules: [streamingApi] });
//     await streamingApi.liveVideoStreams.PushStream(...);

import { defineRpcService, RpcRemoteExecutionMode, RpcType, type RpcHub } from 'actuallab-rpc';
import { Api, type ApiModule } from './api.js';
import type { Moment } from './rpc-scalars.js';
import { coreApi } from './core-api.js';

// Streaming push calls: fire-and-forget.  AwaitForConnection lets us wait for the WS to
// come up before initial send; AllowReconnect makes the $sys.Reconnect protocol skip the
// call on same-peer reconnect (server still has the handler, stream resumes via ACK).
// We deliberately DO NOT set AllowResend: on peer change the call + stream fail, and
// the caller recreates them.  Mirror of [RpcMethod] on the .NET interfaces.
const StreamPushMode = RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect;
const StreamControlMode = RpcRemoteExecutionMode.AwaitForConnection;

// `clientStartAt` is the source's Unix-epoch capture timestamp (seconds, double).

// --- ILiveVideoStreams (per-stream video push/pull + quality control) ---
export const LiveVideoStreamsDef = defineRpcService('ILiveVideoStreams', {
    GetStream: { args: ['session', 'streamId'], returns: RpcType.stream },
    PushStream: {
        args: ['session', 'chatId', 'clientStartAt', 'format', 'sourceKind', 'frameStream'],
        remoteExecutionMode: StreamPushMode,
    },
    RequestKeyFrame: { args: ['session', 'streamId'], remoteExecutionMode: StreamControlMode },
    ChangeRecordingQuality: { args: ['session', 'state', 'info'], remoteExecutionMode: StreamControlMode },
    ChangePlaybackQuality: { args: ['session', 'qualityByStream', 'info'], remoteExecutionMode: StreamControlMode },
});

// --- ILiveAudioStreams (per-stream audio push/pull + transcripts) ---
export const LiveAudioStreamsDef = defineRpcService('ILiveAudioStreams', {
    GetStream: { args: ['session', 'streamId', 'skipTo'], returns: RpcType.stream },
    GetTranscriptStream: { args: ['session', 'streamId'], returns: RpcType.stream },
    PushStream: {
        args: ['session', 'chatId', 'repliedChatEntryId', 'clientStartAt', 'preSkip', 'frameStream'],
        remoteExecutionMode: StreamPushMode,
    },
    ReportAudioLatency: { args: ['session', 'latency'] },
});

// --- IStreamServer (v2.6 client compat — audio + transcript only) ---
export const StreamServerDef = defineRpcService('IStreamServer', {
    PushAudio: {
        args: ['session', 'chatId', 'repliedChatEntryId', 'clientStartAt', 'preSkip', 'frameStream'],
        remoteExecutionMode: StreamPushMode,
    },
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
    /** Sender's MonotonicClock epoch at capture. Increments on sleep/wake or
     *  NTP step. Receiver resets decode-side anchors when this changes. */
    OffsetEpoch?: number;
    /** Per-layer pointer to this frame's keyframe: the keyframe's own `Index`.
     *  A frame is a keyframe iff `KeyFrameIndex === Index`. There is no
     *  separate IsKeyFrame on the wire — derive it. */
    KeyFrameIndex?: number;
    /** Sender-assigned source-moment counter. Gaps in `Index` between
     *  consecutive same-layer frames == frames dropped somewhere upstream. */
    Index?: number;
    Width?: number;
    Height?: number;
    // SVC layer ID (uint8 on wire). 0 = base (lowest-res) layer,
    // 1+ = higher-res layers. Always 0 on single-encoder (P2P) streams.
    LayerId?: number;
    // Canonical ladder size = max canonical layer id + 1 (uint8 on wire).
    // Layer ids are stable; used by the server forwarder to clamp fan-out.
    LayerCount?: number;
    // Bitmask of canonical layer ids currently encoded (bit i = layer i live).
    // Omitted/0 = legacy sender: every layer in [0, LayerCount) is live.
    LayerMask?: number;
    MaxLayerWidth?: number;
    MaxLayerHeight?: number;
    // Wire-compat only: legacy SVC temporal fields. Ignored by current
    // pipeline; preserved so older publishers can still be parsed.
    TemporalLayerId?: number;
    TemporalLayerCount?: number;
    Codec?: string | null;
    Description?: Uint8Array | null;
    // FrameDropStage[] (byte enum). One entry per dropped predecessor frame
    // tagged with the stage that dropped it.
    DropTrace?: Uint8Array | null;
    // Quarter-turn CW the receiver should apply to display upright (0..3).
    // Omitted from the wire when 0.
    Rotation?: number;
}

// --- VideoFrameBundle TypeScript interface ---
// Matches .NET ActualChat.Video.VideoFrameBundle over MessagePack.
// Carries 1..3 per-layer VideoFrames sharing the same source moment.
// Used only on the publisher → server leg (PushStream); server → consumer
// stays per-frame.
export interface VideoFrameBundleDto {
    Layers: VideoFrameDto[];
}

// --- Size2D / VideoFormat TypeScript interfaces ---
// Match .NET ActualChat.Media.Size2D and ActualChat.Video.VideoFormat
// over MessagePack. Both use [Key(N)] attributes — MessagePack serializes
// these as PascalCase property keys when string-key resolvers are in
// play (ActualLab's RPC pipeline uses `MessagePackByteSerializer` which
// derives keys from the property names regardless of the [Key] index).
//
// Important: `Width`/`Height` (and the source-side equivalents) on
// `VideoFormat` are NOT flat — they live nested under `Size` and
// `SourceSize`. Sending them flat (the older shape) leaves the server's
// `VideoFormat.Size` defaulted to (0, 0), which breaks downstream
// width/height filters even though the call dispatches.
export interface Size2DDto { Width: number; Height: number }

export interface VideoFormatDto {
    Codec: string;
    CodecSettings: string;
    LayerId?: number;
    Size: Size2DDto;
    SourceSize: Size2DDto;
}

// --- AudioFrame TypeScript interface ---
// Matches .NET AudioFrame serialized via MessagePack with implicit string keys.
export interface AudioFrameDto {
    Data: Uint8Array;
    Offset: Moment;
    Duration: Moment;
    IsKeyFrame: boolean;
}

// --- ReceiveQuality / RecordingQuality / PlaybackQuality DTOs ---
// Match the new .NET quality control records under
// ActualChat.Streaming (Api.Contracts/Streaming/Quality/*.cs) — all use
// MessagePack with explicit numeric Key(N), so wire keys are integers.

export interface ReceiveQualityDto {
    0: number;  // LayerId
}

export interface RecordingQualityStateDto {
    0: number;  // TargetLayerCount
    1: number;  // EffectiveLayerCount
}

export interface RecorderHealthSnapshotDto {
    0: number;   // EncodeDeficitEma
    1: number;   // EncodeDeficitP90
    2: number;   // SlotReplacementRateEma
    3: number;   // SenderFrameDropRatioEma
    4: number;   // LastAckAgeMs
    5: boolean;  // IsConnected
}

export interface RecordingQualityInfoDto {
    0: number;                       // RecordingQualityReason (enum ordinal)
    1: RecorderHealthSnapshotDto;    // Health
}

export interface PlaybackStreamInfoDto {
    0: number;   // IncomingByteRate
    1: number;   // BufferSpanMsEma
    2: number;   // KeyframeSkipsInWindow
    3: number;   // DecoderQueueDepthEma
    4: number;   // CurrentLayerCount
    5: number;   // PlaybackStreamPriority (0=Secondary, 1=Primary)
    6: number;   // Verdict (-1, 0, +1)
}

export interface PlaybackQualityInfoDto {
    0: number;                                  // EstimatedCapacityBytesPerSec
    1: number;                                  // AggregateHealth
    2: number;                                  // PlaybackQualityReason (enum ordinal)
    3: boolean;                                 // IsColdStart
    4: Map<string, PlaybackStreamInfoDto>;      // Streams (ApiMap → MessagePack Map)
}

// --- Typed client interfaces ---

export interface LiveVideoStreamsClient {
    GetStream(session: string, streamId: string): Promise<AsyncIterable<VideoFrameDto>>;
    PushStream(
        session: string,
        chatId: string,
        sourceStartOffsetSeconds: number,
        format: VideoFormatDto,
        sourceKind: number,
        frameStreamRef: unknown): Promise<void>;
    RequestKeyFrame(session: string, streamId: string): Promise<void>;
    ChangeRecordingQuality(
        session: string,
        state: RecordingQualityStateDto | null,
        info: RecordingQualityInfoDto | null): Promise<void>;
    ChangePlaybackQuality(
        session: string,
        qualityByStream: Map<string, ReceiveQualityDto> | null,
        info: PlaybackQualityInfoDto | null): Promise<void>;
}

export interface LiveAudioStreamsClient {
    GetStream(session: string, streamId: string, skipToTicks: Moment): Promise<AsyncIterable<AudioFrameDto>>;
    GetTranscriptStream(session: string, streamId: string): Promise<AsyncIterable<unknown>>;
    PushStream(
        session: string,
        chatId: string,
        repliedChatEntryId: string | null,
        sourceStartOffsetSeconds: number,
        preSkip: number,
        frameStreamRef: unknown): Promise<void>;
    ReportAudioLatency(session: string, latencyTicks: Moment): Promise<void>;
}

// --- Typed proxy for IStreamServer (v2.6 audio path only) ---
export interface StreamServerClient {
    PushAudio(
        session: string,
        chatId: string,
        repliedChatEntryId: string | null,
        sourceStartOffsetSeconds: number,
        preSkip: number,
        frameStreamRef: unknown): Promise<void>;
}

/** Streaming module — pass the `streamingApi` singleton (below) to `Api.init`
 *  and reach typed services through it, e.g.
 *  `streamingApi.liveVideoStreams.PushStream(...)`. */
class StreamingApi implements ApiModule {
    readonly deps = [coreApi];
    register(hub: RpcHub): void {
        // Pre-populate the method registry so compact-format hash resolution
        // works from the very first outbound message.
        hub.registry.registerService(LiveVideoStreamsDef.name, LiveVideoStreamsDef.methods);
        hub.registry.registerService(LiveAudioStreamsDef.name, LiveAudioStreamsDef.methods);
        hub.registry.registerService(StreamServerDef.name, StreamServerDef.methods);
    }

    private _liveVideoStreams: LiveVideoStreamsClient | undefined;
    get liveVideoStreams(): LiveVideoStreamsClient {
        return this._liveVideoStreams
            ??= Api.hub.addClient<LiveVideoStreamsClient>(Api.peer, LiveVideoStreamsDef);
    }

    private _liveAudioStreams: LiveAudioStreamsClient | undefined;
    get liveAudioStreams(): LiveAudioStreamsClient {
        return this._liveAudioStreams
            ??= Api.hub.addClient<LiveAudioStreamsClient>(Api.peer, LiveAudioStreamsDef);
    }

    private _streamServer: StreamServerClient | undefined;
    /** Typed `IStreamServer` client bound to the shared default peer. v2.6 path
     *  only — new code should use `liveAudioStreams` / `liveVideoStreams`. */
    get streamServer(): StreamServerClient {
        return this._streamServer
            ??= Api.hub.addClient<StreamServerClient>(Api.peer, StreamServerDef);
    }
}

export const streamingApi = new StreamingApi();
