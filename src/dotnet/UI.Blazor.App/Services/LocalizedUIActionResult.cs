using ActualLab.Fusion.UI;

namespace ActualChat.UI.Blazor.App.Services;

// An IUIActionResult whose Error carries a localized message, delegating everything else to the
// original failed result. IUIActionResult.Error (and the underlying Result) is immutable, so this
// decorator is how LocalizingUIActionFailureTracker surfaces translated text through the standard
// IUIActionResult.Error.Message path without any consumer-side (ToastHost) changes.
public sealed class LocalizedUIActionResult(IUIActionResult inner, string localizedMessage) : IUIActionResult
{
    private readonly LocalizedError _error = new(localizedMessage, inner.Error!);

    public long ActionId => inner.ActionId;
    public UIAction UntypedAction => inner.UntypedAction;
    public ICommand Command => inner.Command;
    public Moment StartedAt => inner.StartedAt;
    public Moment CompletedAt => inner.CompletedAt;
    public TimeSpan Duration => inner.Duration;
    public CancellationToken CancellationToken => inner.CancellationToken;

    public object? Value => null;
    public Exception? Error => _error;
    public bool HasValue => false;
    public bool HasError => true;

    public void Deconstruct(out object? untypedValue, out Exception? error) {
        untypedValue = null;
        error = _error;
    }

    public object? GetUntypedValueOrErrorBox() => inner.GetUntypedValueOrErrorBox();
}
