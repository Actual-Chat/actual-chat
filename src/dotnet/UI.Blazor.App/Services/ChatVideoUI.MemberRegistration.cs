using ActualChat.Streaming;
using Microsoft.JSInterop;
using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    // Well inside LiveVideoBackend.MemberStalenessThreshold (90s), so a member
    // never ages out mid-call.
    private static readonly TimeSpan MemberRegistrationPeriod = TimeSpan.FromSeconds(20);
    private static readonly string JSGetSupportedDecoderCodecsMethod =
        $"{BlazorUIAppModule.ImportName}.getSupportedDecoderCodecs";

    // Registration follows video-session membership, not the camera. What is
    // registered is DECODE capability, which is a property of viewers: a
    // participant watching with their camera off is exactly who the negotiation
    // protects, and tying this to the recorder would leave them invisible while
    // the senders upgrade to a codec they cannot play.
    private static readonly string JSInitMethod =
        $"{BlazorUIAppModule.ImportName}.initVideoMemberRegistration";

    // Bumped when this client's decode capability changes under it — today only
    // the debug overrides do that, and they need the server to see the new set
    // now rather than at the next heartbeat. JSInvokable on this service rather
    // than on a recorder: the client most likely to force a codec is a viewer
    // with its camera off, which has no recorder to route the call through.
    [JSInvokable]
    public void RequestMemberReregistration()
        => _memberRegistrationEpoch.Value++;

    // Deliberately not swallowing a failure: this chain runs under RetryForever,
    // which only re-runs on a thrown exception. Logging and continuing would
    // leave the hook unpublished for the whole session, silently dropping every
    // override back to the heartbeat.
    private Task PublishRegistrationHook(CancellationToken cancellationToken)
    {
        _registrationHookRef ??= DotNetObjectReference.Create(this);
        return Hub.JS.InvokeVoidAsync(JSInitMethod, cancellationToken, _registrationHookRef).AsTask();
    }

    [ComputeMethod]
    protected virtual async Task<(ChatId? ChatId, int Epoch)> GetMemberRegistrationInput(
        CancellationToken cancellationToken)
    {
        var chatId = await GetActiveVideoChatId(cancellationToken).ConfigureAwait(false);
        var epoch = await _memberRegistrationEpoch.Use(cancellationToken).ConfigureAwait(false);
        return (chatId, epoch);
    }

    private async Task SyncMemberRegistration(CancellationToken cancellationToken)
    {
        await PublishRegistrationHook(cancellationToken).ConfigureAwait(false);

        var cInput = await Computed
            .Capture(() => GetMemberRegistrationInput(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        // SystemClock, not CpuClock: the server ages members by wall clock, and a
        // stopwatch does not advance through an OS suspend — a laptop waking
        // after 90s would think its registration was still fresh while the
        // server had already dropped it.
        var clock = Clocks.SystemClock;
        ChatId? registeredChatId = null;
        var registeredEpoch = -1;
        var registeredAt = default(Moment);
        try {
            while (!cancellationToken.IsCancellationRequested) {
                cInput = await cInput.Update(cancellationToken).ConfigureAwait(false);
                var (activeChatId, epoch) = cInput.Value;
                if (registeredChatId is { } previous && previous != activeChatId) {
                    await UnregisterMember(previous).ConfigureAwait(false);
                    registeredChatId = null;
                }
                if (activeChatId is not { } chatId) {
                    await cInput.When(x => x.ChatId is not null, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // GetActiveVideoChatId is invalidated far more often than the
                // registration changes, so re-register only when the chat is
                // new or the heartbeat is due — otherwise this is an RPC every
                // few seconds for the life of the call.
                var isDue = registeredChatId != chatId
                    || epoch != registeredEpoch
                    || clock.Now - registeredAt >= MemberRegistrationPeriod;
                if (isDue) {
                    var codecs = await JS
                        .InvokeAsync<string[]>(JSGetSupportedDecoderCodecsMethod, cancellationToken)
                        .ConfigureAwait(false);
                    await LiveVideoStreams
                        .RegisterMember(Session, chatId, new ApiArray<string>(codecs), cancellationToken)
                        .ConfigureAwait(false);
                    if (registeredChatId != chatId)
                        Log.LogInformation("SyncMemberRegistration({ChatId}): decoder codecs=[{Codecs}]",
                            chatId, string.Join(", ", codecs));
                    registeredChatId = chatId;
                    registeredEpoch = epoch;
                    registeredAt = clock.Now;
                }

                // Re-registers on a timer rather than only on change: the server
                // drops members that go stale, and an invalidation lost anywhere
                // along the chain would otherwise never heal.
                using var waitCts = cancellationToken.CreateLinkedTokenSource();
                try {
                    await cInput.WhenInvalidated(waitCts.Token)
                        .WaitAsync(MemberRegistrationPeriod, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException) { }
                finally {
                    waitCts.CancelAndDisposeSilently();
                }
            }
        }
        finally {
            // Leaving a member behind would keep narrowing the codec set for
            // everyone still in the call.
            if (registeredChatId is { } last)
                await UnregisterMember(last).ConfigureAwait(false);
            _registrationHookRef.DisposeSilently();
            _registrationHookRef = null;
        }
    }

    private async Task UnregisterMember(ChatId chatId)
    {
        try {
            await LiveVideoStreams.UnregisterMember(Session, chatId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "UnregisterMember({ChatId}) failed", chatId);
        }
    }
}
