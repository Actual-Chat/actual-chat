using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

/// <summary>
/// Routes <see cref="IncomingCallUI"/> to CallKit. The lock-screen members are Android
/// choreography: iOS has no keyguard to dismiss and CallKit owns the call screen, so
/// they are deliberately inert here.
/// </summary>
public sealed class IosIncomingCallsBridge : IIncomingCallsBridge
{
    public bool OwnsRinging => true;

    public void StartRinging()
    { }

    public void StopRinging()
        // The reactive ring-end - remote cancel, RingTimeout, answered on another device.
        // DismissCallNotification covers only what the user does on this device, so without
        // this the CallKit screen outlives a call nobody is still ringing.
        => IosCalls.Instance.EndActiveCalls();

    public Task<ChatId[]> ListActiveCallChatIds(CancellationToken cancellationToken)
        => Task.FromResult(IosCalls.Instance.ListActiveCallChatIds());

    public void DismissCallNotification(ChatId chatId)
        => IosCalls.Instance.EndCall(chatId);

    // No keyguard on iOS: the call screen is CallKit's, and the app is never brought over
    // a lock screen to show one. True means "go ahead and start audio".
    public Task<bool> OnCallHandled(bool accepted)
        => Task.FromResult(accepted);

    public void RevealCallScreen()
    { }

    public void MoveBehindLockScreen()
    { }
}
