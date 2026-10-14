namespace ActualChat.Hosting;

/// <summary>
/// Two states the host keeps apart: <see cref="IsBackground"/> is "the OS may suspend us",
/// <see cref="IsHidden"/> is "nobody can see the page". They coincide on phones and on the web;
/// a desktop app with its window hidden, minimized or covered is hidden but never backgrounded.
/// </summary>
public abstract class BackgroundStateTracker
{
    public abstract IState<bool> IsBackground { get; }
    public abstract IState<bool> IsHidden { get; }
}
