// Fusion RPC service definitions needed by the load test.
// These mirror the C# IEmailAuth / ILiveVideoStreams / IStreamServer
// contracts. ILiveVideoStreams / IStreamServer already live in
// `../src/api/streaming-service.ts` — we duplicate them here so the test has zero
// dependencies on the shared api tree and can be built stand-alone.

import { defineRpcService, RpcRemoteExecutionMode, RpcType } from '../src/actuallab-rpc/index.js';

export type Int64 = number | bigint;

export function toInt64(value: number): bigint {
    return BigInt(Math.trunc(value));
}

export function int64ToNumber(value: Int64): number {
    return typeof value === 'bigint' ? Number(value) : value;
}

// --- IEmailAuth (commander-backed sign-in) ---
// Server method:
//   Task<bool> OnValidateTotp(EmailAuth_ValidateTotp command, CancellationToken ct);
// Command record (PascalCase wire keys via [MessagePackObject(true)]):
//   EmailAuth_ValidateTotp { Session: string; Email: string; Totp: int }
// Wire: "IEmailAuth.OnValidateTotp:2" (1 command arg + CancellationToken slot).
export const EmailAuthDef = defineRpcService('IEmailAuth', {
    OnValidateTotp: { args: ['command'] },
});

export interface EmailAuthValidateTotpCommand {
    Session: string;
    Email: string;
    Totp: number;
}

export interface EmailAuthClient {
    OnValidateTotp(command: EmailAuthValidateTotpCommand): Promise<boolean>;
}

// --- ILiveVideoStreams (pull + discovery) ---
// Wire (pull): "ILiveVideoStreams.GetStream:4" — (session, streamId, skipTo) + CT.
// Wire (list): "ILiveVideoStreams.List:3" — (session, chatId) + CT.
// skipTo is .NET TimeSpan ticks (int64).
//
// `List` is a [ComputeMethod] on the server. Fusion RPC rejects the call
// unless the wire envelope carries CallTypeId = RpcCallTypeIds.Compute (= 1);
// see RpcInboundContext.cs:48 which hard-matches CallType.Id and rejects
// mismatches with a (confusingly rendered) `Invalid CallTypeId` error. The
// base RpcHub._createClientMethod forwards this callTypeId into the outbound
// message envelope — enough for the dispatch check to pass. We don't track
// invalidation (no RpcOutboundComputeCall equivalent in TS), but for a
// one-shot polling query the server-side `SendResult()` still fires so we
// get the value back on $sys.Ok like any other call.
export const LiveVideoStreamsDef = defineRpcService('ILiveVideoStreams', {
    GetStream: { args: ['session', 'streamId'], returns: RpcType.stream },
    List: { args: ['session', 'chatId'], callTypeId: 1 },
});

export interface VideoFrameDto {
    Data: Uint8Array;
    Offset: Int64;
    Duration: Int64;
    IsKeyFrame: boolean;
    Width?: number;
    Height?: number;
    Description?: Uint8Array | null;
    Codec?: string | null;
    TemporalLayerId?: number;
}

export interface Size2D {
    Width: number;
    Height: number;
}

export interface VideoFormat {
    Codec: string;
    CodecSettings: string;
    SpatialLayerId: number;
    Size: Size2D;
    SourceSize: Size2D;
}

export interface VideoStreamInfo {
    StreamId: string;
    ChatId: string;
    AuthorId: string;
    Formats: VideoFormat[];
    StartedAt: unknown;
    StreamKind?: number;
}

export interface LiveVideoStreamsClient {
    GetStream(session: string, streamId: string): Promise<AsyncIterable<VideoFrameDto>>;
    List(session: string, chatId: string): Promise<VideoStreamInfo[]>;
}

// --- IStreamServer (push) ---
// Wire: "IStreamServer.PushVideo:7" — (session, chatId, clientStartAt, format, frameStream, streamKind) + CT.
// Must match the [RpcMethod] mode on IStreamServer.cs.
export const StreamServerDef = defineRpcService('IStreamServer', {
    PushVideo: {
        args: ['session', 'chatId', 'clientStartAt', 'format', 'frameStream', 'streamKind'],
        remoteExecutionMode: RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect,
    },
});

export interface StreamServerClient {
    PushVideo(
        session: string,
        chatId: string,
        clientStartAt: number, // Unix epoch (seconds, double)
        format: VideoFormat,
        frameStreamRef: unknown,
        streamKind: number,
    ): Promise<void>;
}
