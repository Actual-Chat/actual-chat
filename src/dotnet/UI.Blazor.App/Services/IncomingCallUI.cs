using System.Collections.Immutable;
using ActualChat.Live;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo);

/// <summary>
/// Client-side incoming-ring state: a push (or notification reconciliation) triggers
/// <see cref="OnRing"/>, but the reactive <see cref="LiveSessionUI.Get"/> is the source
/// of truth — a ring ends itself on cancel, timeout, decline, or accept on another device.
/// </summary>
public class IncomingCallUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private readonly Lock _lock = new();
    private readonly MutableState<ImmutableList<ChatId>> _ringingChatIds;

    private IIncomingCallsBridge? Bridge { get; }
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IAuthors Authors => Hub.Authors;

    public IncomingCallUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _ringingChatIds = StateFactory.NewMutable(
            ImmutableList<ChatId>.Empty,
            StateCategories.Get(GetType(), "RingingChatIds"));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public void OnRing(ChatId chatId)
    {
        if (chatId is null || chatId.Value.IsNullOrEmpty())
            return;

        lock (_lock) {
            var chatIds = _ringingChatIds.Value;
            if (!chatIds.Contains(chatId))
                _ringingChatIds.Value = chatIds.Add(chatId);
        }
    }

    public void OnCallDismissed(ChatId chatId)
    {
        if (chatId is null || chatId.Value.IsNullOrEmpty())
            return;

        EndRing(chatId);
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetIncomingCall(CancellationToken cancellationToken)
    {
        var chatIds = await _ringingChatIds.Use(cancellationToken).ConfigureAwait(false);
        for (var i = chatIds.Count - 1; i >= 0; i--) {
            var call = await GetRingingCall(chatIds[i], cancellationToken).ConfigureAwait(false);
            if (call is not null)
                return call;
        }
        return null;
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetRingingCall(ChatId chatId, CancellationToken cancellationToken)
    {
        var live = await LiveSessionUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (ownAuthor is null)
            return null;

        return FindRingingCall(live, ownAuthor.Id);
    }

    public static IncomingCall? FindRingingCall(LiveSession? live, AuthorId ownAuthorId)
    {
        if (live is not { Kind: LiveSessionKind.Call })
            return null;
        if (live.Host == ownAuthorId)
            return null;

        var invite = live.Invites.FirstOrDefault(i => i.InviteeId == ownAuthorId);
        if (invite is not { Status: CallInviteStatus.Ringing })
            return null;

        return new IncomingCall(live.ChatId, live.Host, live.Rules.VideoAllowed);
    }

    public async Task Accept(ChatId chatId, bool withCamera = false)
    {
        var call = await GetRingingCall(chatId, default).ConfigureAwait(true);
        EndRing(chatId);
        if (call is null) {
            Hub.ToastUI.Show("Call ended", "icon-phone", ToastDismissDelay.Short);
            return;
        }

        try {
            await LiveSessionUI.AcceptCall(chatId, default).ConfigureAwait(true);
        }
        catch (Exception e) {
            Log.LogWarning(e, "AcceptCall failed for chat #{ChatId}", chatId);
            Hub.ToastUI.Show("Call ended", "icon-phone", ToastDismissDelay.Short);
            return;
        }

        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
        if (await Hub.AudioRecorder.MicrophonePermission.CheckOrRequest(CancellationToken.None).ConfigureAwait(true))
            await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
        else
            // Mic denied: still join the call as a listener.
            await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(true);
        _ = withCamera; // Camera-on accept is wired in the camera-preview task.
    }

    public async Task Decline(ChatId chatId)
    {
        EndRing(chatId);
        try {
            await LiveSessionUI.DeclineCall(chatId, default).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "DeclineCall failed for chat #{ChatId}", chatId);
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(SyncRings)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 10), Log)
            .Run(cancellationToken);

    // Private methods

    private async Task SyncRings(CancellationToken cancellationToken)
    {
        if (Bridge is not null)
            // A call push may have landed while the app was killed and the user opened it
            // from the launcher — pick the ring up from the still-active system notification.
            foreach (var chatId in await Bridge.ListActiveCallChatIds(cancellationToken).ConfigureAwait(false))
                OnRing(chatId);

        var cCall = await Computed
            .Capture(() => GetIncomingCall(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var isRinging = false;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var call = cCall.Value;
                if (call is not null != isRinging) {
                    isRinging = call is not null;
                    if (isRinging)
                        Bridge?.StartRinging();
                    else
                        Bridge?.StopRinging();
                }
                await PruneDeadRings(cancellationToken).ConfigureAwait(false);

                await cCall.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                cCall = await cCall.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            if (isRinging)
                Bridge?.StopRinging();
        }
    }

    private async Task PruneDeadRings(CancellationToken cancellationToken)
    {
        // Dead rings would otherwise accumulate for the whole scope lifetime; a still-live
        // second ring survives the prune and surfaces once the current one ends.
        ImmutableList<ChatId> chatIds;
        lock (_lock)
            chatIds = _ringingChatIds.Value;
        foreach (var chatId in chatIds) {
            if (await GetRingingCall(chatId, cancellationToken).ConfigureAwait(false) is not null)
                continue;

            lock (_lock)
                _ringingChatIds.Value = _ringingChatIds.Value.Remove(chatId);
        }
    }

    private void EndRing(ChatId chatId)
    {
        lock (_lock) {
            var chatIds = _ringingChatIds.Value;
            if (chatIds.Contains(chatId))
                _ringingChatIds.Value = chatIds.Remove(chatId);
        }
        Bridge?.DismissCallNotification(chatId);
    }
}
