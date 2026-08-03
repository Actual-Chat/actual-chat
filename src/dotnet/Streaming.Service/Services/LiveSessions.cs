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
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private ILiveAudioBackend LiveAudioBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private ILiveVideoBackend LiveVideoBackend => field ??= Services.GetRequiredService<ILiveVideoBackend>();
    private ILiveSessionsBackend Backend => field ??= Services.GetRequiredService<ILiveSessionsBackend>();

    // [ComputeMethod]
    public virtual async Task<LiveSessionState?> GetState(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return await Backend.GetState(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<LiveSession?> Get(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return await Backend.Get(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> HasRecorder(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return await Backend.HasRecorder(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> HasActivity(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        var audioStreams = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        if (audioStreams.Count != 0)
            return true;

        var videoStreams = await LiveVideoBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        return videoStreams.Count != 0;
    }

    // [ComputeMethod]
    public virtual async Task<CallStatus> GetCallStatus(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        var callState = await Backend.GetCallState(chatId, cancellationToken).ConfigureAwait(false);
        // Only the caller sees the status of their outgoing call.
        return callState is not null && callState.CallerId == chat.Rules.Author?.Id
            ? callState.Status
            : CallStatus.None;
    }

    public async Task DismissCallStatus(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        if (await GetCallStatus(session, chatId, cancellationToken).ConfigureAwait(false) != CallStatus.None)
            await Backend.DismissCallStatus(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetParticipation(
        Session session,
        ChatId chatId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (!chat.IsMember())
            return;

        var authorId = chat.Rules.Author!.Id;
        await Backend.SetParticipation(chatId, authorId, kind, isActive, cancellationToken).ConfigureAwait(false);
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
        if (chat.Rules.Author?.Id != targetAuthorId) {
            RequireNotPeerChat(chatId);
            await RequireManage(session, chatId, cancellationToken).ConfigureAwait(false);
        }
        await Backend.MutePeer(chatId, targetAuthorId, muted, cancellationToken).ConfigureAwait(false);
    }

    public async Task MuteAll(Session session, ChatId chatId, bool muted, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        RequireNotPeerChat(chatId);
        await RequireManage(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat.Rules.Author?.Id is not { } ownAuthorId)
            return;

        await Backend.MuteAll(chatId, ownAuthorId, muted, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartCall(
        Session session,
        ChatId chatId,
        ApiArray<AuthorId> invitees,
        bool hasVideo,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (!chat.IsMember())
            return;

        // Same anti-spam gate as peer messaging: in a peer chat the audio/video (and other stream)
        // permissions are stripped unless the recipient stored the caller's contact or replied to
        // them (a block by the recipient leaves the contact non-regular too). So CanWriteAudio is the
        // reused signal that this caller is allowed to reach the peer with a call.
        if (chatId is PeerChatId && !chat.Rules.CanWriteAudio())
            throw StandardError.Constraint(
                "You can call this user only after they add you to their contacts or reply to you.");

        var callerAuthorId = chat.Rules.Author!.Id;
        if (invitees.Count == 0) {
            // Empty = ring every other chat member.
            var allAuthorIds = await Authors
                .ListAuthorIds(session, chatId, cancellationToken)
                .ConfigureAwait(false);
            invitees = allAuthorIds.Where(id => id != callerAuthorId).ToApiArray();
        }
        await Backend
            .StartCall(chatId, callerAuthorId, invitees, hasVideo, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AcceptCall(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        if (await RequireOwnAuthorId(session, chatId, cancellationToken).ConfigureAwait(false) is { } authorId)
            await Backend.AcceptCall(chatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeclineCall(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        if (await RequireOwnAuthorId(session, chatId, cancellationToken).ConfigureAwait(false) is { } authorId)
            await Backend.DeclineCall(chatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelCall(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        if (await RequireOwnAuthorId(session, chatId, cancellationToken).ConfigureAwait(false) is { } authorId)
            await Backend.CancelCall(chatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    public async Task LeaveCall(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        if (await RequireOwnAuthorId(session, chatId, cancellationToken).ConfigureAwait(false) is { } authorId)
            await Backend.LeaveCall(chatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<AuthorId?> RequireOwnAuthorId(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return chat.Rules.Author?.Id;
    }

    // Host or chat Owner may manage the live session.
    private async Task RequireManage(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (chat.Rules.IsOwner())
            return;
        var live = await Backend.GetState(chatId, cancellationToken).ConfigureAwait(false);
        if (live?.Host is { } host && chat.Rules.Author?.Id is { } actingAuthorId && host == actingAuthorId)
            return;
        throw StandardError.Constraint("Only the call host or a chat Owner can manage the live session.");
    }

    private static void RequireNotPeerChat(ChatId chatId)
    {
        // A 1:1 conversation has no host in any meaningful sense - neither side may silence the other.
        if (chatId is PeerChatId)
            throw StandardError.Constraint("You cannot mute another participant in a one-on-one chat.");
    }
}
