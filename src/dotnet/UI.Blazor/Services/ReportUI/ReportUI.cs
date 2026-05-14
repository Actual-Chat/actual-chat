using ActualLab.IO;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Submits a user-initiated diagnostic report with a log-file attachment.
/// Caller owns <c>logFile</c> and may delete it once <c>Submit</c> returns.
/// The base implementation is a no-op; hosts override it with a real transport.
/// </summary>
public class ReportUI
{
    public virtual bool IsAvailable => false;

    public virtual Task Submit(string comment, FilePath logFile, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
