namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform hook for incoming-call rings: the looping ringtone/vibration and the
/// system call-notification bookkeeping. Registered on Android only; when absent,
/// <see cref="IncomingCallUI"/> is inert past its computed state.
/// </summary>
public interface IIncomingCallsBridge
{
    void StartRinging();
    void StopRinging();
    Task<ChatId[]> ListActiveCallChatIds(CancellationToken cancellationToken);
    void DismissCallNotification(ChatId chatId);
}
