namespace ActualChat.UI.Blazor.Services;

public abstract record HistoryState
{
    public virtual double Priority => 0;
    public virtual int BackCount => 0; // How many times you can "Back" from this state

    public abstract HistoryState Apply(HistoryChange change);
    public virtual HistoryState Fix(HistoryChange change)
        => this;
}
