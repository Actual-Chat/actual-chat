namespace ActualChat.UI.Blazor.Services;

public abstract class RenderVar(Symbol name)
{
    public Symbol Name { get; } = name;
    public abstract object UntypedValue { get; }
    public event Action<RenderVar>? Changed;

    public void NotifyChanged()
        => Changed?.Invoke(this);
}

public sealed class RenderVar<T>(Symbol name, T value) : RenderVar(name)
{
    private T _value = value;

    public T Value {
        get => _value;
        set {
            _value = value;
            NotifyChanged();
        }
    }

    // ReSharper disable once HeapView.PossibleBoxingAllocation
    public override object UntypedValue => Value!;
}
