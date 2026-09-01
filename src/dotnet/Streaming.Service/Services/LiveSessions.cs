using ActualChat.Audio;
using ActualChat.Live;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Public facade for live-conversation activity in a chat: the in-progress block and join/leave.
/// </summary>
public class LiveSessions(IServiceProvider services) : ILiveSessions
{
    private static readonly TileLayer<long> EntryIdTiles = Constants.Chat.EntryIdTiles;

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
    private IAudioStreamingBackend AudioStreamingBackend
        => field ??= Services.GetRequiredService<IAudioStreamingBackend>();

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
    public virtual async Task<ConversationStats?> GetConversationStats(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        // The permission check stays a real dependency while the stats themselves are polled:
        // losing access must reach the caller now, not on the next period.
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (!chat.Rules.Has(ChatPermissions.ReadAudio))
            return null;

        return await GetConversationStatsByChat(chatId, cancellationToken).ConfigureAwait(false);
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

    // Session-less for the same reason as GetAudioStreamingAuthorIdsByChat. Every read below is
    // isolated and the self-invalidation is this method's only invalidation source: transcript
    // snapshots re-arm a few times a second and the chat's tail tile moves with every entry, so
    // depending on either would make "re-measured once per period" meaningless.
    [ComputeMethod]
    protected virtual async Task<ConversationStats?> GetConversationStatsByChat(
        ChatId chatId, CancellationToken cancellationToken)
    {
        Computed.GetCurrent().Invalidate(AudioSettings.ConversationStatsPeriod);
        using var _ = Computed.BeginIsolation();

        var state = await Backend.GetState(chatId, cancellationToken).ConfigureAwait(false);
        // A session that never latched to 2+ authors isn't a conversation - one person streaming
        // into an empty chat is exactly what the reminder this feeds exists for.
        if (state?.SessionStartedAt is not { } startedAt)
            return null;

        var now = Clocks.ServerClock.Now;
        var from = Moment.Max(startedAt, now - AudioSettings.ConversationWindow);
        var speechDurations = new Dictionary<AuthorId, double>();
        var transcriptSizes = new Dictionary<AuthorId, int>();
        await AddLiveStreams(chatId, from, now, speechDurations, transcriptSizes, cancellationToken)
            .ConfigureAwait(false);
        await AddFinalizedEntries(chatId, from, speechDurations, transcriptSizes, cancellationToken)
            .ConfigureAwait(false);
        return new ConversationStats {
            Duration = now - startedAt,
            SpeechDurations = new ApiMap<AuthorId, double>(speechDurations),
            TranscriptSizes = new ApiMap<AuthorId, int>(transcriptSizes),
        };
    }

    // Session-less on purpose: the author set spans every participant, so keying it by session
    // would give each viewer its own subscription and miss the others' changes.
    [ComputeMethod]
    protected virtual async Task<ApiArray<AuthorId>> GetAudioStreamingAuthorIdsByChat(
        ChatId chatId, CancellationToken cancellationToken)
    {
        // Ordered by stream start so authors appear in the order they began speaking (latest last),
        // and so the consolidation comparer sees a stable sequence rather than Redis order.
        var streams = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        return streams
            .OrderBy(x => x.BeginsAt)
            .ThenBy(x => x.StreamId, StringComparer.Ordinal)
            .Select(x => x.AuthorId)
            .Distinct()
            .ToApiArray();
    }

    // Private methods

    // The in-progress half of the window. Its counterpart skips still-streaming entries, so an
    // utterance is counted here or there, never twice.
    private async Task AddLiveStreams(
        ChatId chatId,
        Moment from,
        Moment now,
        Dictionary<AuthorId, double> speechDurations,
        Dictionary<AuthorId, int> transcriptSizes,
        CancellationToken cancellationToken)
    {
        var streams = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        if (streams.Count == 0)
            return;

        var transcripts = await streams
            .Select(x => AudioStreamingBackend.GetTranscriptSnapshot(StreamId.Parse(x.StreamId), cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        for (var i = 0; i < streams.Count; i++) {
            var stream = streams[i];
            var beginsAt = Moment.Max(stream.BeginsAt, from);
            if (now > beginsAt)
                speechDurations[stream.AuthorId] = speechDurations.GetValueOrDefault(stream.AuthorId)
                    + (now - beginsAt).TotalSeconds;
            if (transcripts[i] is { Length: > 0 } transcript)
                transcriptSizes[stream.AuthorId] = transcriptSizes.GetValueOrDefault(stream.AuthorId)
                    + transcript.Length;
        }
    }

    private async Task AddFinalizedEntries(
        ChatId chatId,
        Moment from,
        Dictionary<AuthorId, double> speechDurations,
        Dictionary<AuthorId, int> transcriptSizes,
        CancellationToken cancellationToken)
    {
        var lidRange = await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        var maxEntries = AudioSettings.MaxConversationEntries;
        var tile = EntryIdTiles.GetTile(lidRange.End - 1);
        // Bounds the tail walk: a window this dense is a conversation many times over, so stopping
        // early can only under-report a chat that already passed every threshold.
        for (var seen = 0; seen < maxEntries;) {
            var chatTile = await ChatsBackend
                .GetTile(chatId, tile.Range, false, cancellationToken)
                .ConfigureAwait(false);
            var isWindowPassed = false;
            for (var i = chatTile.Entries.Length - 1; i >= 0; i--) {
                var entry = chatTile.Entries[i];
                seen++;
                var endsAt = entry.EndsAt;
                if (endsAt < from) {
                    isWindowPassed = true;
                    break;
                }
                // HasAudio, not any text entry: a typed message says nothing about whether the
                // user can hear a conversation happening. Still-streaming ones belong to AddLiveStreams.
                if (!entry.HasAudio || entry.IsContentStreaming || endsAt is not { } entryEndsAt)
                    continue;

                var beginsAt = Moment.Max(entry.BeginsAt, from);
                if (entryEndsAt > beginsAt)
                    speechDurations[entry.AuthorId] = speechDurations.GetValueOrDefault(entry.AuthorId)
                        + (entryEndsAt - beginsAt).TotalSeconds;
                if (entry.Content.Length > 0)
                    transcriptSizes[entry.AuthorId] = transcriptSizes.GetValueOrDefault(entry.AuthorId)
                        + entry.Content.Length;
            }
            if (isWindowPassed || tile.Range.Start <= lidRange.Start)
                break;

            tile = EntryIdTiles.GetTile(tile.Range.Start - 1);
        }
    }

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
