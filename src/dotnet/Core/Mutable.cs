namespace ActualChat;

public static class Mutable
{
    public static Mutable<T>New<T>(T initialValue) => new(initialValue);
}

public sealed class Mutable<T>(T initialValue = default!)
{
    private T _value = initialValue;

    public T Value {
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
