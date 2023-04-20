namespace ActualChat.UI.Blazor.Services;

public abstract class RenderVar
{
    public Symbol Name { get; }
    public abstract object UntypedValue { get; }
    public event Action<RenderVar>? Changed;

    protected RenderVar(Symbol name)
        => Name = name;

    public void NotifyChanged()
        => Changed?.Invoke(this);
}

public sealed class RenderVar<T> : RenderVar
{
    private T _value;

    public T Value {
        get => _value;
        set {
            _value = value;
            NotifyChanged();
        }
    }

    // ReSharper disable once HeapView.PossibleBoxingAllocation
    public override object UntypedValue => Value!;

    public RenderVar(Symbol name, T value) : base(name)
        => _value = value;
}
