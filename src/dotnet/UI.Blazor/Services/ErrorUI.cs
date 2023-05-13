namespace ActualChat.UI.Blazor.Services;

public class ErrorUI
{
    private readonly UIActionTracker _uiActionTracker;

    public ErrorUI(UIActionTracker uiActionTracker)
        => _uiActionTracker = uiActionTracker;

    /// <summary>
    /// Shows an error the same way command errors are shown.
    /// </summary>
    /// <param name="error">The error to show.</param>
    public void ShowError(Exception error) {
        // use command error reporting approach
        var command = new ErrorCommand();
        var clock = _uiActionTracker.Clock;
        var errorTask = Task.FromException<Unit>(error);
        _uiActionTracker.Register(new UIAction<Unit>(command, clock, errorTask, CancellationToken.None));
    }

    // Nested types

    private sealed record ErrorCommand : ICommand<Unit>;
}
