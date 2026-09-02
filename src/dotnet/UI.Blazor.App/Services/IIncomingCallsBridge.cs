namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform hook for incoming-call rings: the looping ringtone/vibration and the
/// system call-notification bookkeeping. Registered on Android only; when absent,
/// <see cref="IncomingCallUI"/> is inert past its computed state.
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
    void DismissCallNotification(ChatId chatId);
    // Resolves the over-lock-screen call UI. On accept it dismisses the keyguard so the user lands
    // in the app; the returned task completes once the screen is unlocked (or immediately if it
    // wasn't locked) and its result says whether the app is now foreground-ready to start the audio
    // foreground service — false when the user cancelled unlocking, so the caller must not start it
    // from a background state. On a non-accept end it releases the app from over the lock screen.
    Task<bool> OnCallHandled(bool accepted);
    // Called once the call screen has actually rendered: brings the app over the keyguard (for a warm
    // start, where it wasn't shown over-lock eagerly to avoid a cover) and removes the cold-start
    // cover. So the lock screen reveals the drawn call screen, never the app's content.
    void RevealCallScreen();
    // On hang-up from the over-lock-screen call UI: drops the over-keyguard flag and sends the app
    // behind the lock screen (moveTaskToBack) so the user returns straight to the lock screen.
    void MoveBehindLockScreen();
}
