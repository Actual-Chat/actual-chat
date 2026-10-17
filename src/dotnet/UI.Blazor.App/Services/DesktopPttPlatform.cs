namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform hooks for push-to-talk in a desktop app, which has no wake path: armed chats are listened
/// to for as long as the app runs. Only Windows and macOS register one.
/// </summary>
public abstract class DesktopPttPlatform
{
    // Do Not Disturb, a Focus, a presentation: armed chats don't play on their own while it's true.
    // Polled, so it must be cheap, and it fails open - one bad read must not silently kill PTT.
    public abstract bool IsQuiet { get; }

    public virtual void OnIsArmedChanged(bool isArmed)
    { }
}
