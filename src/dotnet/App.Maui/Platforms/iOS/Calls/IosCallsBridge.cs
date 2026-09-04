using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;

namespace ActualChat.App.Maui;

/// <summary>
/// Routes <see cref="IncomingCallUI"/> and outgoing <see cref="LiveSessionUI"/> calls to
/// CallKit. The lock-screen members are Android choreography: iOS has no keyguard to dismiss
/// and CallKit owns the call screen, so they are deliberately inert here.
/// </summary>
public sealed class IosCallsBridge : IIncomingCallsBridge, ISystemCallUI, IDisposable
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _stopTokenSource = new();
    private readonly ConcurrentDictionary<ChatId, Unit> _watchedChatIds = new();
    private AppUIHub Hub { get; }
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ILogger Log => field ??= StaticLog.For<IosCallsBridge>();

    public bool OwnsRinging => true;

    public IosCallsBridge(AppUIHub hub)
    {
        Hub = hub;
        // The calls are static but the watch is not: a WebView reload (or a wake scope handing
        // over to the WebView one) takes the previous bridge's watch with it, and an answered
        // call is skipped by EndRingingCalls, so nothing else would ever take it down.
        foreach (var chatId in IosCalls.Instance.ListCallsNeedingWatch())
            StartCallEndWatch(chatId);
    }

    public void Dispose()
        // Only the watch dies here: a scope replacement mid-call is not a hang-up, and the
        // incoming scope's bridge re-arms from IosCalls.
        => _stopTokenSource.CancelAndDisposeSilently();

    public void StartRinging()
    { }

    public void StopRinging()
        // The reactive ring-end - remote cancel, RingTimeout, answered on another device. Ringing
        // calls only: on iOS the CallKit call IS the call, so one the user just answered has to
        // outlive its ring.
        => IosCalls.Instance.EndRingingCalls();

    public Task<ChatId[]> ListActiveCallChatIds(CancellationToken cancellationToken)
        => Task.FromResult(IosCalls.Instance.ListActiveCallChatIds());

    public void DismissCallNotification(ChatId chatId)
        // Not an end: the ring bookkeeping fires this for an accept exactly as it does for a
        // decline, and OnCallHandled is what carries the verdict.
        => IosCalls.Instance.MarkRingHandledLocally(chatId);

    public Task<bool> OnCallHandled(ChatId chatId, bool accepted)
    {
        if (!accepted) {
            IosCalls.Instance.DeclineCall(chatId);
            return Task.FromResult(false);
        }

        if (IosCalls.Instance.AnswerCall(chatId))
            StartCallEndWatch(chatId);

        // No keyguard on iOS: the call screen is CallKit's, and the app is never brought over
        // a lock screen to show one. True means "go ahead and start audio".
        return Task.FromResult(true);
    }

    public void RevealCallScreen()
    { }

    public void MoveBehindLockScreen()
    { }

    // ISystemCallUI

    public void OnOutgoingCallStarted(ChatId chatId, bool hasVideo)
        // Fire-and-forget: placing the call must not wait on the chat lookup the name comes from.
        => _ = BackgroundTask.Run(
            () => StartOutgoingCall(chatId, hasVideo, _stopTokenSource.Token),
            Log, $"Couldn't start an outgoing CallKit call for chat #{chatId}", _stopTokenSource.Token);

    public void OnOutgoingCallStatusChanged(ChatId chatId, CallStatus status)
    {
        // Answered: the same watch an incoming call gets, so leaving the conversation ends the
        // CallKit call rather than stranding it.
        if (IosCalls.Instance.ReportOutgoingCallStatus(chatId, status))
            StartCallEndWatch(chatId);
    }

    public void OnOutgoingCallCancelled(ChatId chatId)
        => IosCalls.Instance.CancelOutgoingCall(chatId);

    // Private methods

    private async Task StartOutgoingCall(ChatId chatId, bool hasVideo, CancellationToken cancellationToken)
    {
        var chat = await Hub.Chats.Get(Hub.Session, chatId, cancellationToken).ConfigureAwait(false);
        IosCalls.Instance.StartOutgoingCall(chatId, chat?.Title ?? "", hasVideo);
    }

    private void StartCallEndWatch(ChatId chatId)
    {
        if (!_watchedChatIds.TryAdd(chatId, default))
            return;

        _ = BackgroundTask.Run(
            () => WatchCallEnd(chatId, _stopTokenSource.Token),
            Log, $"Call-end watch failed for chat #{chatId}", _stopTokenSource.Token);
    }

    private async Task WatchCallEnd(ChatId chatId, CancellationToken cancellationToken)
    {
        // A watch that gives up strands an answered CallKit call for good - AmIInLiveConversation
        // sits on an RPC-backed chain that faults transiently - so every failure here is retried.
        // The join deadline is wall-clock, so the retries can't extend it; the wait for the end is
        // unbounded.
        var startedAt = CpuTimestamp.Now;
        var retryDelays = RetryDelaySeq.Exp(0.5, 10);
        var hasJoined = false;
        try {
            for (var tryIndex = 0;; tryIndex++) {
                try {
                    hasJoined = hasJoined
                        || await WhenJoined(chatId, JoinTimeout - startedAt.Elapsed, cancellationToken)
                            .ConfigureAwait(false);
                    if (!hasJoined) {
                        // Answered but never joined: nothing else would ever take this call down,
                        // and the CallKit screen would sit over the app until the user ends it.
                        IosCalls.Instance.FailCall(chatId);
                        return;
                    }

                    // Being in the conversation IS the verdict: a scope replaced inside the Accept
                    // window leaves the ring's verdict unresolved, and nothing else delivers it.
                    IosCalls.Instance.AnswerCall(chatId);
                    await WhenLeft(chatId, cancellationToken).ConfigureAwait(false);
                    // One watch covers every way an answered call ends: in-app hang-up, remote
                    // hang-up, and the session ending on its own.
                    IosCalls.Instance.EndCall(chatId);
                    return;
                }
                catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                    Log.LogWarning(e, "Call-end watch for chat #{ChatId} failed, retrying", chatId);
                    await Task.Delay(retryDelays[tryIndex], cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally {
            _watchedChatIds.TryRemove(chatId, out _);
        }
    }

    private async Task<bool> WhenJoined(ChatId chatId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            return false;

        var cIsInCall = await Computed
            .Capture(() => LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        try {
            // The (value, error) overload: an error means "keep waiting", where the plain one
            // would rethrow it and end the watch.
            await cIsInCall
                .When((x, error) => error is null && x, cancellationToken)
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException) {
            return false;
        }
    }

    private async Task WhenLeft(ChatId chatId, CancellationToken cancellationToken)
    {
        var cIsInCall = await Computed
            .Capture(() => LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        // Only a successful false ends the call: an error here must not be read as a hang-up.
        await cIsInCall
            .When((x, error) => error is null && !x, cancellationToken)
            .ConfigureAwait(false);
    }
}
