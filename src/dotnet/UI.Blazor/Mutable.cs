namespace ActualChat.UI.Blazor;

public sealed class Mutable<T>
{
    private T _value = default!;

    public T Value
    {
        get => _value;
        set => Set(value);
    }

    public event Action<T, T>? Changed;

    // This is a convenience helper that allows to use mutable?.Set(...)
    public void Set(T value)
    {
        var oldValue = _value;
        _value = value;
        if (!EqualityComparer<T>.Default.Equals(oldValue, value))
            Changed?.Invoke(oldValue, value);
    }
}
