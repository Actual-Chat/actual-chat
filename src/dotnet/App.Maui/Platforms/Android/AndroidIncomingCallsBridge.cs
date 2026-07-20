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
        AppServicesAccessor.BeginDispatchToMainThread(() => {
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

    public Task<ChatId[]> ListActiveCallChatIds(CancellationToken cancellationToken)
        => Task.FromResult(IncomingCallNotifications.ListActiveCallChatIds());

    public void DismissCallNotification(ChatId chatId)
        => IncomingCallNotifications.Dismiss(chatId);

    public void Dispose()
        => IncomingCallRinger.Stop();
}
