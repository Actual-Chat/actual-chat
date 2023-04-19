using System.Threading.Tasks.Sources;

namespace ActualChat;

/// <summary>
/// <see cref="IValueTaskSource{TResult}" /> with pooling. <br/>
/// NOTE: You should either await created <see cref="ValueTask"/> directly
/// (optionally with <c>.ConfigureAwait(false)</c>) or call <c>AsTask()</c> on it directly, and then never use it again.<br/>
/// <see href="https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Sources/ManualResetValueTaskSourceCore.cs" />
/// <see href="https://github.com/dotnet/aspnetcore/blob/main/src/Shared/ServerInfrastructure/ManualResetValueTaskSource.cs" />
/// </summary>
public sealed class ValueTaskSource<T> : IValueTaskSource<T>, IValueTaskSource
{
    internal IValueTaskSourceFactory<T>? Factory;
    private ManualResetValueTaskSourceCore<T> _core; // mutable struct; do not make this readonly

    public bool RunContinuationsAsynchronously { get => _core.RunContinuationsAsynchronously; set => _core.RunContinuationsAsynchronously = value; }
    public short Version => _core.Version;
    public void Reset() => _core.Reset();
    public void SetResult(T result) => _core.SetResult(result);
    public void SetException(Exception error) => _core.SetException(error);

    public T GetResult(short token)
    {
        try {
            var ret = _core.GetResult(token);
            return ret;
        }
        finally {
            // shouldn't be null here, if so it's better to fail fast with NRE
            Factory!.Return(this);
            // just in case if someone stores the ValueTask with our source (shouldn't do this anyway)
            Factory = null;
        }
    }
    void IValueTaskSource.GetResult(short token) => _core.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);

    public ValueTaskSourceStatus GetStatus() => _core.GetStatus(_core.Version);

    public void TrySetResult(T result)
    {
        if (_core.GetStatus(_core.Version) == ValueTaskSourceStatus.Pending) {
            _core.SetResult(result);
        }
    }
}
