namespace ActualChat.Hosting;

public abstract class BackgroundStateTracker
{
    public abstract IState<bool> IsBackground { get; }
}
