using ActualChat.Streaming;

namespace ActualChat.Testing.Host;

public static class LiveSessionsBackendTestExt
{
    private static readonly ConcurrentDictionary<ChatId, CallId> LastCallIds = new();

    // The calls these tests drive have no client behind them, so every client of the user sees them
    public static async Task<CallId> StartCall(
        this ILiveSessionsBackend backend,
        ChatId chatId,
        AuthorId callerAuthorId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        CancellationToken cancellationToken)
    {
        var callId = await backend
            .StartCall(chatId, callerAuthorId, invitees, hasVideo, null, null, cancellationToken)
            .ConfigureAwait(false);
        LastCallIds[chatId] = callId;
        return callId;
    }

    // Naming no call, these act on the last call placed in the chat - as the client told of it would
    public static async Task AcceptCall(
        this ILiveSessionsBackend backend,
        ChatId chatId,
        AuthorId inviteeAuthorId,
        CancellationToken cancellationToken)
    {
        var callId = await backend.GetCallId(chatId, cancellationToken).ConfigureAwait(false);
        await backend.AcceptCall(chatId, inviteeAuthorId, null, null, callId, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task DeclineCall(
        this ILiveSessionsBackend backend,
        ChatId chatId,
        AuthorId inviteeAuthorId,
        CancellationToken cancellationToken)
    {
        var callId = await backend.GetCallId(chatId, cancellationToken).ConfigureAwait(false);
        await backend.DeclineCall(chatId, inviteeAuthorId, callId, cancellationToken).ConfigureAwait(false);
    }

    public static async Task CancelCall(
        this ILiveSessionsBackend backend,
        ChatId chatId,
        AuthorId callerAuthorId,
        CancellationToken cancellationToken)
    {
        var callId = await backend.GetCallId(chatId, cancellationToken).ConfigureAwait(false);
        await backend.CancelCall(chatId, callerAuthorId, callId, cancellationToken).ConfigureAwait(false);
    }

    // The id StartCall above answered with is preferred to a read: GetCall isn't one a test can make
    // unnoticed - it syncs the call's status and expires its rings. A chat in no call gets an id no
    // call has: the request is then one for a call that is over.
    private static async Task<CallId> GetCallId(
        this ILiveSessionsBackend backend,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        if (LastCallIds.TryGetValue(chatId, out var callId))
            return callId;

        var call = await backend.GetCall(chatId, cancellationToken).ConfigureAwait(false);
        return call?.Id ?? CallId.New(chatId, "none");
    }
}
