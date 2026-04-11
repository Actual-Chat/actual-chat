// Fusion RPC service definitions needed by the load test.
// These mirror the C# IEmailAuth / ISecureTokens / ILiveVideoStreams / IStreamServer
// contracts. The existing UI.Blazor.App defines ILiveVideoStreams / IStreamServer in
// `video-rpc-service.ts` — we duplicate them here so the test has zero dependencies
// on the Blazor app's source tree and can be built stand-alone.

import { defineRpcService, RpcType } from '../src/actuallab-rpc/index.js';

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

// --- ISecureTokens ---
// Server method: Task<SecureToken> CreateForSession(Session session, CancellationToken ct);
// Session serializes as a plain string (see SessionMessagePackFormatter).
// Returns SecureToken { Token: string; ExpiresAt: Moment }. We only need Token.
// Wire: "ISecureTokens.CreateForSession:2".
export const SecureTokensDef = defineRpcService('ISecureTokens', {
    CreateForSession: { args: ['session'] },
});

export interface SecureTokenDto {
    Token: string;
    ExpiresAt: unknown;
}

export interface SecureTokensClient {
    CreateForSession(session: string): Promise<SecureTokenDto>;
}

// --- ILiveVideoStreams (pull + discovery) ---
// Wire (pull): "ILiveVideoStreams.GetStream:4" — (session, streamId, skipTo) + CT.
// Wire (list): "ILiveVideoStreams.List:3" — (session, chatId) + CT.
// skipTo is .NET TimeSpan ticks (int64).
export const LiveVideoStreamsDef = defineRpcService('ILiveVideoStreams', {
    GetStream: { args: ['session', 'streamId', 'skipTo'], returns: RpcType.stream },
    List: { args: ['session', 'chatId'] },
});

export interface VideoFrameDto {
    Data: Uint8Array;
    Offset: number;
    Duration: number;
    IsKeyFrame: boolean;
    Width?: number;
    Height?: number;
    Description?: Uint8Array | null;
    Codec?: string | null;
    TemporalLayerId?: number;
}

export interface VideoFormatDto {
    Codec: string;
    Width: number;
    Height: number;
    CodecSettings: string;
}

export interface VideoStreamInfoDto {
    StreamId: string;
    ChatId: string;
    AuthorId: string;
    Format: VideoFormatDto;
    StartedAt: unknown;
    StreamKind?: number;
}

export interface LiveVideoStreamsClient {
    GetStream(session: string, streamId: string, skipToTicks: number): Promise<AsyncIterable<VideoFrameDto>>;
    List(session: string, chatId: string): Promise<VideoStreamInfoDto[]>;
}

// --- IStreamServer (push) ---
// Wire: "IStreamServer.PushVideo:7" — (session, chatId, clientStartOffset, format, continuationOf, frameStream) + CT.
export const StreamServerDef = defineRpcService('IStreamServer', {
    PushVideo: { args: ['session', 'chatId', 'clientStartOffset', 'format', 'continuationOf', 'frameStream'] },
});

export interface StreamServerClient {
    PushVideo(
        session: string,
        chatId: string,
        clientStartOffset: number,
        format: VideoFormatDto,
        continuationOf: string | null,
        frameStreamRef: unknown,
    ): Promise<void>;
}
