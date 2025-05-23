namespace ActualChat.UI.Blazor.Services;

public abstract class RenderVar(string name)
{
    public string Name { get; } = name;
    public abstract object UntypedValue { get; }
    public event Action<RenderVar>? Changed;

    public void NotifyChanged()
        => Changed?.Invoke(this);
}

public sealed class RenderVar<T>(string name, T value) : RenderVar(name)
{
    public T Value {
        get;
        set {
            field = value;
            NotifyChanged();
        }
    } = value;

    // ReSharper disable once HeapView.PossibleBoxingAllocation
    public override object UntypedValue => Value!;
}
