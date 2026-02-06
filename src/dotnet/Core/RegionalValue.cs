namespace ActualChat;

/// <summary>
/// A scoped value that can be temporarily changed and automatically restored on dispose.
/// </summary>
public sealed class RegionalValue<T>
{
    public T Value { get; private set; }

    public RegionalValue(T value)
        => Value = value;

    public ClosedDisposable<(RegionalValue<T>, T)> Change(T value)
    {
        var oldValue = Value;
        Value = value;
        return new ClosedDisposable<(RegionalValue<T>, T)>((this, oldValue), static arg => {
            var (self, oldValue1) = arg;
            self.Value = oldValue1;
        });
    }
}
