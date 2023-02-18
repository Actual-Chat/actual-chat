namespace ActualChat;

public sealed class LocalValue<T>
{
    public T Value { get; set; }

    public LocalValue(T value)
        => Value = value;

    public ClosedDisposable<(LocalValue<T>, T)> Change(T value)
    {
        var oldValue = Value;
        Value = value;
        return new ClosedDisposable<(LocalValue<T>, T)>((this, oldValue), static arg => {
            var (self, oldValue1) = arg;
            self.Value = oldValue1;
        });
    }
}
