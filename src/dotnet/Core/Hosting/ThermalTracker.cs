namespace ActualChat.Hosting;

public abstract class ThermalTracker
{
    public abstract IState<ThermalLevel> Level { get; }
}
