// Fusion RPC service definitions needed by the load test.
// These mirror the C# IEmailAuth / ILiveVideoStreams contracts.
// ILiveVideoStreams already lives in `../src/api/streaming-service.ts` — we
// duplicate it here so the test has zero dependencies on the shared api tree
// and can be built stand-alone.

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
    PushStream: {
        args: ['session', 'chatId', 'clientStartAt', 'format', 'sourceKind', 'frameStream'],
        remoteExecutionMode: RpcRemoteExecutionMode.AwaitForConnection | RpcRemoteExecutionMode.AllowReconnect,
    },
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
}

export interface Size2D {
    Width: number;
    Height: number;
}

export interface VideoFormat {
    Codec: string;
    CodecSettings: string;
    LayerId: number;
    Size: Size2D;
    SourceSize: Size2D;
}

export interface VideoStreamInfo {
    StreamId: string;
    ChatId: string;
    AuthorId: string;
    Formats: VideoFormat[];
    StartedAt: unknown;
    SourceKind?: number;
}

export interface LiveVideoStreamsClient {
    GetStream(session: string, streamId: string): Promise<AsyncIterable<VideoFrameDto>>;
    List(session: string, chatId: string): Promise<VideoStreamInfo[]>;
    PushStream(
        session: string,
        chatId: string,
        clientStartAt: number, // Unix epoch (seconds, double)
        format: VideoFormat,
        sourceKind: number,
        frameStreamRef: unknown,
    ): Promise<void>;
}
