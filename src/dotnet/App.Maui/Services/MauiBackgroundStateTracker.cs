using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

// Must be singleton!
public class MauiBackgroundStateTracker : BackgroundStateTracker, IDisposable
{
    private static readonly Mutable<bool?> IsBackgroundSource = new();

    private readonly MutableState<bool> _isBackgroundState;

    public override IState<bool> IsBackground => _isBackgroundState;

    public MauiBackgroundStateTracker(IServiceProvider services)
    {
        lock (IsBackgroundSource) {
            _isBackgroundState = services.StateFactory().NewMutable(
                IsBackgroundSource.Value ?? false,
                StateCategories.Get(GetType(), nameof(IsBackground)));
            IsBackgroundSource.Changed += OnSetBackgroundState;
        }
    }

    public void Dispose()
    {
        lock (IsBackgroundSource)
            IsBackgroundSource.Changed -= OnSetBackgroundState;
    }

    public static void SetBackgroundState(bool isBackground)
    {
        lock (IsBackgroundSource)
            IsBackgroundSource.Value = isBackground;
    }

    // Private methods

    private void OnSetBackgroundState(bool? oldValue, bool? value)
        => _isBackgroundState.Value = value ?? false;
}
