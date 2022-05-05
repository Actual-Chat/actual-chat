namespace ActualChat.UI.Blazor.Services;

public class ErrorUI
{
    private sealed record ErrorSurrogateCommand : ICommand;
    private readonly UICommandRunner _cmd;

    public ErrorUI(UICommandRunner cmd)
        => _cmd = cmd;

    /// <summary>
    /// Shows an error the same way command errors are shown.
    /// </summary>
    /// <param name="error"></param>
    public void ShowError(string error)
        => ShowError(new SurrogateException(error));

    /// <summary>
    /// Shows an error the same way command errors are shown.
    /// </summary>
    /// <param name="error">The error to show.</param>
    public void ShowError(Exception error) {
        // use command error reporting approach
        var result = Result.Error<string>(error);
        var command = new ErrorSurrogateCommand();
        var completedEvent = new UICommandEvent(command, result);
        _ = _cmd.UICommandTracker.ProcessEvent(completedEvent);
    }

    [Serializable]
    public class SurrogateException : Exception
    {
        public SurrogateException() { }
        protected SurrogateException(SerializationInfo info, StreamingContext context) : base(info, context) { }
        public SurrogateException(string? message) : base(message) { }
        public SurrogateException(string? message, Exception? innerException) : base(message, innerException) { }
    }
}
