using ActualChat.Streaming;

namespace ActualChat.Testing.Host;

public static class LiveSessionsBackendTestExt
{
    // The calls these tests drive have no client behind them, so every client of the user sees them
    public static Task StartCall(
        this ILiveSessionsBackend backend,
        ChatId chatId,
        AuthorId callerAuthorId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        CancellationToken cancellationToken)
        => backend.StartCall(chatId, callerAuthorId, invitees, hasVideo, null, null, cancellationToken);

    public static Task AcceptCall(
        this ILiveSessionsBackend backend,
        ChatId chatId,
        AuthorId inviteeAuthorId,
        CancellationToken cancellationToken)
        => backend.AcceptCall(chatId, inviteeAuthorId, null, null, cancellationToken);
}
