using ActualChat.Live;
using ActualChat.Users;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Public facade for live-conversation activity in a chat: the in-progress block and join/leave.
/// </summary>
public class LiveSessions(IServiceProvider services) : ILiveSessions
{
    private IServiceProvider Services { get; } = services;
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private ILiveSessionsBackend Backend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();

    // [ComputeMethod]
    public virtual async Task<LiveSessionState?> Get(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (!chat.Rules.CanRead())
            return null;

        return await Backend.Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<LiveSession?> GetLiveSession(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (!chat.Rules.CanRead())
            return null;

        return await Backend.GetLiveSession(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> HasRecorder(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (!chat.Rules.CanRead())
            return false;

        return await Backend.HasRecorder(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetParticipation(
        Session session,
        ChatId chatId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        await Backend.SetParticipation(chatId, account.Id, kind, isActive, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetRules(Session session, ChatId chatId, SessionRules rules, CancellationToken cancellationToken)
    {
        await RequireManage(session, chatId, cancellationToken).ConfigureAwait(false);
        await Backend.SetRules(chatId, rules, cancellationToken).ConfigureAwait(false);
    }

    public async Task MutePeer(
        Session session,
        ChatId chatId,
        AuthorId targetAuthorId,
        bool muted,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (chat.Rules.Author?.Id != targetAuthorId)
            await RequireManage(session, chatId, cancellationToken).ConfigureAwait(false);
        await Backend.MutePeer(chatId, targetAuthorId, muted, cancellationToken).ConfigureAwait(false);
    }

    public async Task MuteAll(Session session, ChatId chatId, bool muted, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        await RequireManage(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat.Rules.Author?.Id is not { } ownAuthorId)
            return;

        await Backend.MuteAll(chatId, ownAuthorId, muted, cancellationToken).ConfigureAwait(false);
    }

    // Host or chat admin may manage the live session.
    private async Task RequireManage(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (chat.Rules.IsOwner())
            return;
        var live = await Backend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (live?.Host is { } host && chat.Rules.Author?.Id is { } actingAuthorId && host == actingAuthorId)
            return;
        throw StandardError.Constraint("Only the call host or a chat admin can manage the live session.");
    }
}
