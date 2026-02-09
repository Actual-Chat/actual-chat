using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.UI.Blazor;

namespace ActualChat.App.Maui.IosShareExt.UI;

// TODO: if possible get rid of duplicating UIWorkerBase
public abstract class UIWorkerBase(IosHub hub) : UIServiceBase(hub), IUIWorker
{
    private volatile Task? _whenRunning;
    public Task? WhenRunning => _whenRunning;

    public virtual Task Run()
    {
        if (_whenRunning != null)
            return _whenRunning;
        lock (Lock) {
            if (_whenRunning != null)
                return _whenRunning;
            if (StopToken.IsCancellationRequested)
                return _whenRunning = Task.CompletedTask;

            // ReSharper disable once PossibleMultipleWriteAccessInDoubleCheckLocking
            _whenRunning = Task.Run(async () => {
                try {
                    await OnRun(StopToken).ConfigureAwait(false);
                }
                catch {
                    // Intended: WhenRunning should behave similarly
                    // to how it behaves in WorkerBase, i.e. never throw.
                }
            }, CancellationToken.None);
        }
        return _whenRunning;
    }

    protected abstract Task OnRun(CancellationToken cancellationToken);
}
