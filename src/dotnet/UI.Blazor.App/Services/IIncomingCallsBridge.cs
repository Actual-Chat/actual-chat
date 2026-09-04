namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform hook for incoming-call rings: the looping ringtone/vibration and the
/// system call bookkeeping. Registered on Android (notification + ringer) and iOS
/// (CallKit); when absent, <see cref="IncomingCallUI"/> falls back to the web ringtone.
/// </summary>
public interface IIncomingCallsBridge
{
    bool OwnsRinging
        // True on CallKit: gates the web ringtone and the YieldCommunicationMode/RestoreAudioMode
        // pair only — StartRinging/StopRinging are still always called.
        => false;
    void StartRinging();
    void StopRinging();
    Task<ChatId[]> ListActiveCallChatIds(CancellationToken cancellationToken);
    // Fires on every local ring-end - accepted and declined alike - so it carries no verdict.
    void DismissCallNotification(ChatId chatId);
    // Carries the ring's verdict for chatId: accepted or not. On Android it resolves the
    // over-lock-screen call UI — on accept it dismisses the keyguard so the user lands in the app,
    // and the returned task completes once the screen is unlocked (or immediately if it wasn't)
    // with whether the app is now foreground-ready to start the audio foreground service; false
    // when the user cancelled unlocking, so the caller must not start it from a background state.
    // On a non-accept end it releases the app from over the lock screen. On CallKit it mirrors the
    // verdict into the system call, which outlives the ring.
    Task<bool> OnCallHandled(ChatId chatId, bool accepted);
    // Called once the call screen has actually rendered: brings the app over the keyguard (for a warm
    // start, where it wasn't shown over-lock eagerly to avoid a cover) and removes the cold-start
    // cover. So the lock screen reveals the drawn call screen, never the app's content.
    void RevealCallScreen();
    // On hang-up from the over-lock-screen call UI: drops the over-keyguard flag and sends the app
    // behind the lock screen (moveTaskToBack) so the user returns straight to the lock screen.
    void MoveBehindLockScreen();
}
