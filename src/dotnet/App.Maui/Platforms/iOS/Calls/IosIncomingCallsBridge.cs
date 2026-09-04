using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;

namespace ActualChat.App.Maui;

/// <summary>
/// Routes <see cref="IncomingCallUI"/> to CallKit. The lock-screen members are Android
/// choreography: iOS has no keyguard to dismiss and CallKit owns the call screen, so
/// they are deliberately inert here.
/// </summary>
public sealed class IosIncomingCallsBridge(AppUIHub hub) : IIncomingCallsBridge, IDisposable
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _stopTokenSource = new();
    private readonly ConcurrentDictionary<ChatId, Unit> _watchedChatIds = new();
    private AppUIHub Hub { get; } = hub;
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ILogger Log => field ??= StaticLog.For<IosIncomingCallsBridge>();

    public bool OwnsRinging => true;

    public void Dispose()
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

    // Private methods

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
        try {
            var cIsInCall = await Computed
                .Capture(() => LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            try {
                cIsInCall = await cIsInCall
                    .When(x => x, cancellationToken)
                    .WaitAsync(JoinTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) {
                // Answered but never joined: nothing else would ever take this call down, and the
                // CallKit screen would sit over the app for the rest of the scope's life.
                IosCalls.Instance.FailCall(chatId);
                return;
            }

            await cIsInCall.When(x => !x, cancellationToken).ConfigureAwait(false);
            // One watch covers every way an answered call ends: in-app hang-up, remote hang-up,
            // and the session ending on its own.
            IosCalls.Instance.EndCall(chatId);
        }
        finally {
            _watchedChatIds.TryRemove(chatId, out _);
        }
    }
}
