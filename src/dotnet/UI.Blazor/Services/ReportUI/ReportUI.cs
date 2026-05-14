using ActualLab.IO;

namespace ActualChat.UI.Blazor.Services;

public class ReportUI(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    public virtual bool IsAvailable => false;

    public virtual Task Submit(string comment, FilePath logFile, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
