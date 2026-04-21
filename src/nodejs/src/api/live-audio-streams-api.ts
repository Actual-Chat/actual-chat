// Live audio streams RPC module — mirror of .NET `ILiveAudioStreams` (see
// src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs). Provides a typed
// TS client that subscribes to the multiplexed live/replay audio stream and
// returns an `RpcStream<LiveStreamItem>` the consumer can iterate.
//
// Usage:
//     Api.init(url, liveAudioStreamsApi);
//     const stream = await liveAudioStreamsApi.liveAudioStreams
//         .GetStream(session, chatId, { StreamKindFilter: LiveStreamKind.Audio });
//     for await (const item of stream) { ... }

import { defineRpcService, RpcType, type RpcHub } from 'actuallab-rpc';
import type { RpcStream } from 'actuallab-rpc';
import { Api, type ApiModule } from './api.js';
import { coreApi } from './core-api.js';

// --- ILiveAudioStreams (multiplexed live+replay audio) ---
export const LiveAudioStreamsDef = defineRpcService('ILiveAudioStreams', {
    List: { args: ['session', 'chatId'] },
    GetStream: { args: ['session', 'chatId', 'settings'], returns: RpcType.stream },
    ChangeSettings: { args: ['session', 'chatId', 'settings'] },
    GetReplayStream: {
        args: ['session', 'chatId', 'startAt', 'rewindOffset', 'speed'],
        returns: RpcType.stream,
    },
});

// --- LiveStreamKind (matches .NET enum, Flags) ---
export const LiveStreamKind = {
    None: 0,
    Audio: 1,
} as const;
export type LiveStreamKind = number;

// --- LiveStreamSettings ---
// Matches .NET: [MessagePackObject(true)] → PascalCase keys.
export interface LiveStreamSettingsDto {
    StreamKindFilter: LiveStreamKind;
}

// --- AudioCodecKind (matches .NET enum) ---
export const AudioCodecKind = {
    Opus: 0,
} as const;
export type AudioCodecKind = number;

// --- AudioFormat ---
// Matches .NET: [MessagePackObject(true)] → PascalCase keys.
export interface AudioFormatDto {
    ChannelCount: number;
    CodecKind: AudioCodecKind;
    CodecSettings: string;
    SampleRate: number;
    PreSkip: number;
}

// --- LiveStreamInfo ---
export interface LiveStreamInfoDto {
    ChatId: string;
    AuthorId: string;
    StreamId: string;
    BeginsAt: number;           // Moment → int64 ticks (100ns from Unix epoch)
    Format: AudioFormatDto | null;
    EntryId: string | null;
}

// --- LiveStreamItem (union) ---
// Wire format (MessagePack-CSharp [Union]): 2-element fix-array
// [tagIndex, payload]. Tags (from LiveStreamItem.cs):
//   0 = LiveStreamStart
//   1 = LiveStreamEnd
//   2 = LiveAudioFrame
//   3 = LiveStreamReset
// Every subtype's payload carries StreamIndex (Order 0).
export const LiveStreamTag = {
    Start: 0,
    End: 1,
    Frame: 2,
    Reset: 3,
} as const;
export type LiveStreamTag = (typeof LiveStreamTag)[keyof typeof LiveStreamTag];

export interface LiveStreamStartPayload {
    StreamIndex: number;
    StreamInfo: LiveStreamInfoDto;
    PlaysAt: number;    // TimeSpan ticks
}
export interface LiveStreamEndPayload {
    StreamIndex: number;
}
export interface LiveAudioFramePayload {
    StreamIndex: number;
    Data: Uint8Array;
    Offset: number;     // TimeSpan ticks
}
export interface LiveStreamResetPayload {
    StreamIndex: number;
}

export interface LiveStreamStart { tag: 0; payload: LiveStreamStartPayload }
export interface LiveStreamEnd { tag: 1; payload: LiveStreamEndPayload }
export interface LiveAudioFrame { tag: 2; payload: LiveAudioFramePayload }
export interface LiveStreamReset { tag: 3; payload: LiveStreamResetPayload }
export type LiveStreamItem = LiveStreamStart | LiveStreamEnd | LiveAudioFrame | LiveStreamReset;

/**
 * Parse a raw stream item off the wire into a discriminated union. RpcStream
 * items arrive as `[tag, payload]` arrays because the .NET type is decorated
 * with `[Union]`; this helper normalizes that into an `{tag, payload}` object.
 * Returns `null` for malformed items (silently skip them).
 */
export function parseLiveStreamItem(raw: unknown): LiveStreamItem | null {
    if (!Array.isArray(raw) || raw.length !== 2)
        return null;
    const rawArr = raw as unknown[];
    const tag = rawArr[0];
    if (typeof tag !== 'number' || tag < 0 || tag > 3)
        return null;
    const payload = rawArr[1];
    if (typeof payload !== 'object' || payload === null)
        return null;
    return { tag, payload } as LiveStreamItem;
}

// --- Typed proxy for ILiveAudioStreams calls on the client side. ---
export interface LiveStreamInfoListDto {
    // ApiArray<LiveStreamInfo> wire shape depends on the ApiArray formatter.
    // Treating as unknown[] for now; callers can cast when we need it.
    items: LiveStreamInfoDto[];
}

export interface LiveAudioStreamsClient {
    List(session: string, chatId: string): Promise<LiveStreamInfoDto[]>;
    GetStream(
        session: string,
        chatId: string,
        settings: LiveStreamSettingsDto,
    ): Promise<RpcStream<unknown>>;
    ChangeSettings(
        session: string,
        chatId: string,
        settings: LiveStreamSettingsDto,
    ): Promise<void>;
    GetReplayStream(
        session: string,
        chatId: string,
        startAt: bigint,          // Moment ticks
        rewindOffsetTicks: bigint, // TimeSpan ticks
        speed: number,
    ): Promise<RpcStream<unknown>>;
}

/** Live-audio-streams module — register alongside `streamingApi`. */
class LiveAudioStreamsApi implements ApiModule {
    readonly deps = [coreApi];
    register(hub: RpcHub): void {
        hub.registry.registerService(LiveAudioStreamsDef.name, LiveAudioStreamsDef.methods);
    }

    private _client: LiveAudioStreamsClient | undefined;
    get liveAudioStreams(): LiveAudioStreamsClient {
        return this._client
            ??= Api.hub.addClient<LiveAudioStreamsClient>(Api.peer, LiveAudioStreamsDef);
    }
}

export const liveAudioStreamsApi = new LiveAudioStreamsApi();
