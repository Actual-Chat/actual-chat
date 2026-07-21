using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public sealed class AndroidIncomingCallsBridge : IIncomingCallsBridge, IDisposable
{
    private ILogger Log => field ??= StaticLog.For<AndroidIncomingCallsBridge>();

    public void StartRinging()
        => IncomingCallRinger.Start();

    public void StopRinging()
        => IncomingCallRinger.Stop();

    public Task<bool> OnCallHandled(bool accepted)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginDispatchToMainThread(() => {
            try {
                if (accepted)
                    MainActivity.Current.DismissKeyguardForCall(ready => tcs.TrySetResult(ready));
                else {
                    MainActivity.Current.DisableShowWhenLocked();
                    tcs.TrySetResult(false);
                }
            }
            catch (Exception e) {
                Log.LogDebug(e, "OnCallHandled skipped");
                // No activity to gate on: proceed best-effort on accept.
                tcs.TrySetResult(accepted);
            }
        });
        return tcs.Task;
    }

    public void RevealCallScreen()
        => BeginDispatchToMainThread(() => {
            try {
                var activity = MainActivity.Current;
                // Warm start: the call screen has now rendered, so bring the app over the keyguard
                // (it wasn't shown over-lock eagerly to avoid a cover). Cold start: idempotent here,
                // and the cover is removed to reveal the already-drawn call screen.
                activity.EnableShowWhenLocked();
                activity.HideCallCover();
            }
            catch (Exception e) {
                Log.LogDebug(e, "RevealCallScreen skipped");
            }
        });

    public void MoveBehindLockScreen()
        => BeginDispatchToMainThread(() => {
            try {
                var activity = MainActivity.Current;
                activity.DisableShowWhenLocked();
                activity.MoveTaskToBack(true);
            }
            catch (Exception e) {
                Log.LogDebug(e, "MoveBehindLockScreen skipped");
            }
        });

    public Task<ChatId[]> ListActiveCallChatIds(CancellationToken cancellationToken)
        => Task.FromResult(IncomingCallNotifications.ListActiveCallChatIds());

    public void DismissCallNotification(ChatId chatId)
        => IncomingCallNotifications.Dismiss(chatId);

    public void Dispose()
        => IncomingCallRinger.Stop();
}
