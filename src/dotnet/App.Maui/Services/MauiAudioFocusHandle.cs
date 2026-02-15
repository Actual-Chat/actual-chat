namespace ActualChat.App.Maui.Services;

public sealed class MauiAudioFocusHandle(Action<MauiAudioFocusHandle>? release = null)
{
    private static long _lastIndex;

    private long Index { get; } = Interlocked.Increment(ref _lastIndex);
    private readonly Action<MauiAudioFocusHandle> _onRelease = release ?? (_ => { });

    public event Action<bool, bool>? FocusLost;
    public event Action? FocusRecover;

    public void RaiseFocusLost(bool mayRestore, bool canDuck)
        => FocusLost?.Invoke(mayRestore, canDuck);

    public void RaiseFocusRecover()
        => FocusRecover?.Invoke();

    public void Release()
        => _onRelease.Invoke(this);

    public override string ToString()
        => $"{GetType().GetName()}(#{Index})";
}
