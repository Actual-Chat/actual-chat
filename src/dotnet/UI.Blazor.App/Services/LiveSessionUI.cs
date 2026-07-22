using System.Collections.Immutable;
using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// UI-side facade for live conversations: the active block, the local "am I joined" signal
/// (drives per-viewer collapse/expand), and join/leave participation signaling to the server.
/// </summary>
public class LiveSessionUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), IComputeService, INotifyInitialized
{
    // Refresh interval for active participations; must stay under the server's
    // ParticipantStaleness (90s) so a still-joined viewer never expires mid-call.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(45);

    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual async Task<Conversation?> GetConversation(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await LiveSessions.GetState(Session, chatId, cancellationToken).ConfigureAwait(false);
        return state is { SessionStartedAt: not null } ? state.ToConversation() : null;
    }

    [ComputeMethod]
    public virtual Task<LiveSessionState?> GetState(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.GetState(Session, chatId, cancellationToken);

    [ComputeMethod]
    public virtual async Task<LiveSession?> Get(ChatId chatId, CancellationToken cancellationToken)
        => await LiveSessions.Get(Session, chatId, cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> IsTranscriptionOn(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await LiveSessions.GetState(Session, chatId, cancellationToken).ConfigureAwait(false);
        return state?.TranscriptionOn ?? false;
    }

    public Task SetRules(ChatId chatId, SessionRules rules, CancellationToken cancellationToken)
        => LiveSessions.SetRules(Session, chatId, rules, cancellationToken);

    public Task MutePeer(ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken)
        => LiveSessions.MutePeer(Session, chatId, targetAuthorId, muted, cancellationToken);

    public Task MuteAll(ChatId chatId, bool muted, CancellationToken cancellationToken)
        => LiveSessions.MuteAll(Session, chatId, muted, cancellationToken);

    public Task StartCall(ChatId chatId, ApiArray<AuthorId> invitees, bool hasVideo, CancellationToken cancellationToken)
        => LiveSessions.StartCall(Session, chatId, invitees, hasVideo, cancellationToken);

    public Task AcceptCall(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.AcceptCall(Session, chatId, cancellationToken);

    public Task DeclineCall(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.DeclineCall(Session, chatId, cancellationToken);

    public Task CancelCall(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.CancelCall(Session, chatId, cancellationToken);

    public Task LeaveCall(ChatId chatId, CancellationToken cancellationToken)
        => LiveSessions.LeaveCall(Session, chatId, cancellationToken);

    [ComputeMethod]
    public virtual async Task<bool> AmIInLiveConversation(ChatId chatId, CancellationToken cancellationToken)
    {
        var audio = await ChatAudioUI.GetState(chatId).ConfigureAwait(false);
        if (audio.IsListening || audio.IsRecording)
            return true;

        return await ChatVideoUI.IsWatching(chatId, cancellationToken).ConfigureAwait(false);
    }

    public Task SetParticipation(ChatId chatId, ParticipationKind kind, bool isActive, CancellationToken cancellationToken)
        => LiveSessions.SetParticipation(Session, chatId, kind, isActive, cancellationToken);

    protected override async Task OnRun(CancellationToken cancellationToken)
        => await Task.WhenAll(
            RunParticipationSync(cancellationToken),
            RunMuteEnforcement(cancellationToken)).ConfigureAwait(false);

    private async Task RunParticipationSync(CancellationToken cancellationToken)
    {
        var cParticipations = await Computed
            .Capture(() => GetMyParticipations(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var current = new Dictionary<ChatId, ParticipationKind>();
        var lastHeartbeatAt = Clocks.CpuClock.Now;
        while (!cancellationToken.IsCancellationRequested) {
            var next = cParticipations.Value;
            var now = Clocks.CpuClock.Now;
            var isHeartbeat = now - lastHeartbeatAt >= HeartbeatInterval;
            if (isHeartbeat)
                lastHeartbeatAt = now;

            foreach (var chatId in current.Keys.Except(next.Keys).ToList()) {
                await SetParticipation(chatId, current[chatId], false, cancellationToken).ConfigureAwait(false);
                current.Remove(chatId);
            }
            foreach (var (chatId, kind) in next)
                if (isHeartbeat || !current.TryGetValue(chatId, out var existing) || existing != kind) {
                    await SetParticipation(chatId, kind, true, cancellationToken).ConfigureAwait(false);
                    current[chatId] = kind;
                }

            using var cts = cancellationToken.CreateLinkedTokenSource();
            var whenInvalidated = cParticipations.WhenInvalidated(cts.Token);
            var whenHeartbeat = Clocks.CpuClock.Delay(HeartbeatInterval, cts.Token);
            await Task.WhenAny(whenInvalidated, whenHeartbeat).ConfigureAwait(false);
            cts.CancelAndDisposeSilently();
            cParticipations = await cParticipations.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    // Soft mute enforcement: when the host turns off my recording (MicMuted) — either
    // per-peer or via mute-all — my own recorder stops and I'm told why. MicMuted is
    // peer-revocable: tapping record clears it (see RecorderToggle).
    private async Task RunMuteEnforcement(CancellationToken cancellationToken)
    {
        var cMuted = await Computed
            .Capture(() => GetMutedRecordingChat(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            if (cMuted.Value is { } chatId && !chatId.Value.IsNullOrEmpty()) {
                await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
                Hub.ToastUI.Show("Recording turned off by the host", "icon-mic-off", ToastDismissDelay.Short);
            }

            await cMuted.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            cMuted = await cMuted.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    [ComputeMethod]
    protected virtual async Task<ChatId?> GetMutedRecordingChat(CancellationToken cancellationToken)
    {
        var activeChats = await ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        var recording = activeChats.FirstOrDefault(c => c.IsRecording);
        if (recording?.ChatId is not { } chatId || chatId.Value.IsNullOrEmpty())
            return null;

        var live = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (live is null)
            return null;
        var ownAuthor = await Hub.Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (ownAuthor is null)
            return null;
        var me = live.Members.FirstOrDefault(m => m.AuthorId == ownAuthor.Id);
        return me is { MicMuted: true } ? chatId : null;
    }

    [ComputeMethod]
    protected virtual async Task<ImmutableDictionary<ChatId, ParticipationKind>> GetMyParticipations(CancellationToken cancellationToken)
    {
        var result = ImmutableDictionary.CreateBuilder<ChatId, ParticipationKind>();
        var activeChats = await ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        foreach (var chat in activeChats)
            if (chat.IsRecording)
                result[chat.ChatId] = ParticipationKind.Record;
            else if (chat.IsListening)
                result[chat.ChatId] = ParticipationKind.AudioListen;

        // A video publisher (camera or screencast) produces live content even when muted / not
        // audio-recording - report Record so a solo publisher keeps the session live (recorder-driven
        // liveness would otherwise close it once the initial stream registration goes stale).
        var publishingChatId = await ChatVideoUI.GetPublishingChatId(cancellationToken).ConfigureAwait(false);
        if (publishingChatId is { } pubChatId)
            result[pubChatId] = ParticipationKind.Record;

        var watchingChatId = await ChatVideoUI.GetWatchingChatId(cancellationToken).ConfigureAwait(false);
        if (watchingChatId is { } videoChatId && !result.ContainsKey(videoChatId))
            result[videoChatId] = ParticipationKind.VideoView;

        return result.ToImmutable();
    }
}
