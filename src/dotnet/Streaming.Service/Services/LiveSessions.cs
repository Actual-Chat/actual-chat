using ActualChat.Audio;
using ActualChat.Live;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Public facade for live-conversation activity in a chat: the in-progress block and join/leave.
/// </summary>
public class LiveSessions(IServiceProvider services) : ILiveSessions
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;
    // Bounds the tail walk: a window this dense is a conversation many times over, so stopping
    // early can only under-report a chat that already passed every threshold.
    private const int MaxTranscribedTextEntries = 200;

    private IServiceProvider Services { get; } = services;
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private AudioSettings AudioSettings => field ??= Services.GetRequiredService<AudioSettings>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IRolesBackend RolesBackend => field ??= Services.GetRequiredService<IRolesBackend>();
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
    public virtual async Task<ApiArray<AuthorId>> GetAudioStreamingAuthorIds(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        // ReadAudio, not just chat access: this says who is speaking, same as the ILiveAudioStreams.List
        // it replaces for clients.
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (!chat.Rules.Has(ChatPermissions.ReadAudio))
            return default;

        return await GetAudioStreamingAuthorIdsByChat(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiMap<AuthorId, int>> GetTranscribedTextLengths(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return await GetTranscribedTextLengthsByChat(chatId, cancellationToken).ConfigureAwait(false);
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
        var authority = await GetCallAuthority(session, chatId, cancellationToken).ConfigureAwait(false);
        authority.RequireManage();
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
            var authority = await GetCallAuthority(session, chatId, cancellationToken).ConfigureAwait(false);
            authority.RequireManage();
            if (!authority.CanMute(await IsOwner(chatId, targetAuthorId, cancellationToken).ConfigureAwait(false)))
                throw StandardError.Constraint("You can't turn off an Owner's microphone.");
        }

        await Backend.MutePeer(chatId, targetAuthorId, muted, cancellationToken).ConfigureAwait(false);
    }

    public async Task MuteAll(Session session, ChatId chatId, bool muted, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        RequireNotPeerChat(chatId);
        var authority = await GetCallAuthority(session, chatId, cancellationToken).ConfigureAwait(false);
        authority.RequireManage();
        if (authority.OwnAuthorId is not { } ownAuthorId)
            return;

        // A Moderator who is neither an Owner nor the host must leave Owners unmuted.
        var exceptAuthorIds = new ApiArray<AuthorId>([ownAuthorId]);
        if (!authority.CanMute(isTargetOwner: true)) {
            var ownerIds = await RolesBackend
                .ListSystemRoleAuthorIds(ChatsBackend, chatId, SystemRole.Owner, cancellationToken)
                .ConfigureAwait(false);
            exceptAuthorIds = exceptAuthorIds.WithMany(ownerIds.Where(x => x != ownAuthorId).ToArray());
        }
        await Backend.MuteAll(chatId, exceptAuthorIds, muted, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetHost(
        Session session,
        ChatId chatId,
        AuthorId targetAuthorId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        RequireNotPeerChat(chatId);

        // Deliberately not a Moderator power: the host may mute Owners, so letting Moderators
        // hand out that role would make Owner immunity escapable.
        var authority = await GetCallAuthority(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!authority.IsOwner && !authority.IsHost)
            throw StandardError.Constraint("Only the call host or a chat Owner can change the call host.");

        await Backend.SetHost(chatId, targetAuthorId, cancellationToken).ConfigureAwait(false);
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

    // Protected methods

    // Session-less for the same reason as GetAudioStreamingAuthorIdsByChat. The delay throttles
    // the recompute itself: this depends on the chat's tail tile, which every entry invalidates,
    // and a signal measured in minutes has nothing to gain from following that rate.
    [ComputeMethod(InvalidationDelay = 1)]
    protected virtual async Task<ApiMap<AuthorId, int>> GetTranscribedTextLengthsByChat(
        ChatId chatId, CancellationToken cancellationToken)
    {
        // Finalized entries only. A still-streaming entry's text is reachable via
        // IAudioStreamingBackend.GetMergedTranscript, but that re-arms a few times a second while
        // someone speaks, and it would buy 1-3s of text on a window measured in minutes.
        var lidRange = await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        var from = Clocks.ServerClock.Now - AudioSettings.TranscribedTextWindow;
        var lengths = new Dictionary<AuthorId, int>();
        var tile = IdTileStack.FirstLayer.GetTile(lidRange.End - 1);
        for (var seen = 0; seen < MaxTranscribedTextEntries;) {
            var chatTile = await ChatsBackend
                .GetTile(chatId, tile.Range, false, cancellationToken)
                .ConfigureAwait(false);
            var isWindowPassed = false;
            for (var i = chatTile.Entries.Length - 1; i >= 0; i--) {
                var entry = chatTile.Entries[i];
                seen++;
                if (entry.BeginsAt < from) {
                    isWindowPassed = true;
                    break;
                }
                // HasAudio, not any text entry: a typed message says nothing about whether the
                // user can hear a conversation happening.
                if (!entry.HasAudio || entry.Content.Length == 0)
                    continue;

                lengths[entry.AuthorId] = lengths.GetValueOrDefault(entry.AuthorId) + entry.Content.Length;
            }
            if (isWindowPassed || tile.Range.Start <= lidRange.Start)
                break;

            tile = IdTileStack.FirstLayer.GetTile(tile.Range.Start - 1);
        }
        return new ApiMap<AuthorId, int>(lengths);
    }

    // Session-less on purpose: the author set spans every participant, so keying it by session
    // would give each viewer its own subscription and miss the others' changes.
    [ComputeMethod]
    protected virtual async Task<ApiArray<AuthorId>> GetAudioStreamingAuthorIdsByChat(
        ChatId chatId, CancellationToken cancellationToken)
    {
        // Sorted so the consolidation comparer sees a stable sequence rather than Redis order
        var streams = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        return streams
            .Select(x => x.AuthorId)
            .Distinct()
            .OrderBy(x => x.Value)
            .ToApiArray();
    }

    // Private methods

    private async Task<AuthorId?> RequireOwnAuthorId(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return chat.Rules.Author?.Id;
    }

    private async Task<CallAuthority> GetCallAuthority(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        var ownAuthorId = chat.Rules.Author?.Id;
        var live = await Backend.GetState(chatId, cancellationToken).ConfigureAwait(false);
        var isHost = ownAuthorId is not null && live?.Host == ownAuthorId;
        return new CallAuthority(ownAuthorId, chat.Rules.IsOwner(), chat.Rules.CanModerate(), isHost);
    }

    private Task<bool> IsOwner(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
        => RolesBackend.IsInSystemRole(ChatsBackend, authorId, SystemRole.Owner, cancellationToken);

    private static void RequireNotPeerChat(ChatId chatId)
    {
        // A 1:1 conversation has no host in any meaningful sense - neither side may silence the other.
        if (chatId is PeerChatId)
            throw StandardError.Constraint("You cannot mute another participant in a one-on-one chat.");
    }

    // Nested types

    /// <summary>
    /// The union of the actor's call-authority roles: the host runs this call and may mute anyone,
    /// while a Moderator polices the chat and may mute anyone but an Owner.
    /// </summary>
    private readonly record struct CallAuthority(
        AuthorId? OwnAuthorId,
        bool IsOwner,
        bool CanModerate,
        bool IsHost)
    {
        public void RequireManage()
        {
            if (!CanModerate && !IsHost)
                throw StandardError.Constraint(
                    "Only the call host, a chat Owner or Moderator can manage the live session.");
        }

        public bool CanMute(bool isTargetOwner)
            => IsOwner || IsHost || (CanModerate && !isTargetOwner);
    }
}
