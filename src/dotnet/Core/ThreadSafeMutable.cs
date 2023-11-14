namespace ActualChat;

public sealed class ThreadSafeMutable<T>
{
    private readonly object _lock = new();
    private T _value = default!;

    public T Value {
        get => _value;
        set => Set(value);
    }

    public event Action<T, T>? Changed;

    // This is a convenience helper that allows to use mutable?.Set(...)
    public void Set(T value)
    {
        lock (_lock) {
            var oldValue = _value;
            _value = value;
            if (!EqualityComparer<T>.Default.Equals(oldValue, value))
                Changed?.Invoke(oldValue, value);
        }
    }
}
