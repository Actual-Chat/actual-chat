namespace ActualChat.UI.Blazor.Services;

public abstract record HistoryState
{
    public virtual int BackStepCount => 0; // How many times you can "Back" from this state

    public abstract HistoryState Save();
    public abstract void Apply(HistoryTransition transition);
}
