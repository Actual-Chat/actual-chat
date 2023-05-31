namespace ActualChat.Concurrency;

public sealed class NoRecursionRegion
{
    private Action? _exitAction;
    private object Lock { get; }
    private ILogger Log { get; }

    public string Name { get; }
    public bool IsInside => _exitAction != null;

    public Action? ExitAction {
        get => _exitAction;
        set => _exitAction = value ?? throw new ArgumentNullException(nameof(value));
    }

    public NoRecursionRegion(string name, object @lock, ILogger log)
    {
        Name = name;
        Lock = @lock;
        Log = log;
    }

    public ClosedDisposable<NoRecursionRegion> Enter()
    {
        Monitor.Enter(Lock);
        try {
            ThrowIfInside();
            _exitAction = Delegates.Noop;
        }
        catch (Exception) {
            Monitor.Exit(Lock);
            throw;
        }

        return new ClosedDisposable<NoRecursionRegion>(this, static self => {
            var exitAction = self._exitAction;
            self._exitAction = null;
            try {
                if (exitAction != Delegates.Noop)
                    exitAction!.Invoke();
            }
            catch (Exception e) {
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                self.Log.LogError(e, $"{self.Name}: ExitAction failed");
            }
            finally {
                Monitor.Exit(self.Lock);
            }
        });
    }

    public void ThrowIfInside()
    {
        if (IsInside)
            throw StandardError.Constraint($"{Name} cannot be called recursively.");
    }
}
