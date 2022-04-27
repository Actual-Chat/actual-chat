namespace ActualChat.UI.Blazor.Services;

public class ErrorUI
{
    private record ErrorSurrogateCommand : ICommand;
    private readonly UICommandRunner _cmd;

    public ErrorUI(UICommandRunner cmd)
        => _cmd = cmd;

    /// <summary>
    /// Shows error as command error.
    /// </summary>
    /// <param name="error"></param>
    public void ShowError(string error)
        => ShowError(new Exception(error));

    /// <summary>
    /// Shows error as command error.
    /// </summary>
    /// <param name="e"></param>
    public void ShowError(Exception e) {
        // use command error reporting approach
        var result = Result.Error<string>(e);
        var command = new ErrorSurrogateCommand();
        var completedEvent = new UICommandEvent(command, result);
        _ = _cmd.UICommandTracker.ProcessEvent(completedEvent);
    }
}
