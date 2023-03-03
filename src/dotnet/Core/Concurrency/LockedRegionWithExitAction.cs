namespace ActualChat.Concurrency;

public sealed class LockedRegionWithExitAction
{
    private Action? _exitAction;
    private object Lock { get; }

    public string Name { get; }
    public bool IsInside => _exitAction != null;

    public Action? ExitAction {
        get => _exitAction;
        set => _exitAction = value ?? throw new ArgumentNullException(nameof(value));
    }

    public LockedRegionWithExitAction(string name, object @lock)
    {
        Name = name;
        Lock = @lock;
    }

    public ClosedDisposable<LockedRegionWithExitAction> Enter()
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

        return new ClosedDisposable<LockedRegionWithExitAction>(this, static self => {
            var exitAction = self._exitAction;
            self._exitAction = null;
            try {
                if (exitAction != Delegates.Noop)
                    exitAction!.Invoke();
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
