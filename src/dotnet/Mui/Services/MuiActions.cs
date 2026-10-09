using MudBlazor;

namespace ActualChat.Mui;

public sealed class MuiActions(IServiceProvider services)
{
    private ICommander Commander { get; } = services.GetRequiredService<ICommander>();
    private ISnackbar Snackbar { get; } = services.GetRequiredService<ISnackbar>();
    private ILogger Log { get; } = services.LogFor<MuiActions>();

    public async Task<bool> Run(ICommand command, string successMessage = "Done")
    {
        try {
            await Commander.Call(command).ConfigureAwait(false);
            ReportSuccess(successMessage);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            ReportError(e);
            return false;
        }
    }

    public async Task<Result<TResult>> Call<TResult>(ICommand<TResult> command)
    {
        try {
            return await Commander.Call(command).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            ReportError(e);
            return Result.NewError<TResult>(e);
        }
    }

    public void ReportSuccess(string message)
        => Snackbar.Add(message, Severity.Success);

    public void ReportError(Exception error)
    {
        Log.LogWarning(error, "Mui action failed");
        Snackbar.Add(error.Message, Severity.Error);
    }

    public void ReportError(string message)
        => Snackbar.Add(message, Severity.Error);
}
