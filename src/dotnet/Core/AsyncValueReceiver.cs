namespace ActualChat;

public class AsyncValueReceiver<TValue> : IDisposable
{
    private readonly TaskSource<TValue> _whenSetSource;

    public TValue Value {
        get {
            var task = _whenSetSource.Task;
            if (!task.IsCompleted)
                throw StandardError.StateTransition("Value is not set yet.");
 #pragma warning disable VSTHRD002
 #pragma warning disable VSTHRD104
            return task.Result;
 #pragma warning restore VSTHRD104
 #pragma warning restore VSTHRD002
        }
        set => Set(value);
    }

    public TValue? ValueOrDefault {
        get {
            var task = _whenSetSource.Task;
 #pragma warning disable VSTHRD002
 #pragma warning disable VSTHRD104
            return task.IsCompleted ? task.Result : default;
 #pragma warning restore VSTHRD104
 #pragma warning restore VSTHRD002
        }
        set => Set(value!);
    }

    protected AsyncValueReceiver(bool notifyAsynchronously = true)
        => _whenSetSource = TaskSource.New<TValue>(notifyAsynchronously);

    public virtual void Dispose()
        => _whenSetSource.TrySetCanceled();

    public Task<TValue> Get()
        => _whenSetSource.Task;

    public void TrySet(TValue value)
        => _whenSetSource.TrySetResult(value);
    public void Set(TValue value)
        => _whenSetSource.SetResult(value);
}
