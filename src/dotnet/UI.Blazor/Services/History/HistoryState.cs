namespace ActualChat.UI.Blazor.Services;

public abstract record HistoryState
{
    public virtual string Name => GetType().GetName();
    public virtual int BackStepCount => 0; // How many times you can "Back" from this state
    public virtual bool IsUriDependent => false;

    public override string ToString()
        => $"{Name}({Format()})";

    public abstract string Format();
    public abstract HistoryState Save();
    public abstract void Apply(HistoryTransition transition);
    public abstract HistoryState? Back();
}
