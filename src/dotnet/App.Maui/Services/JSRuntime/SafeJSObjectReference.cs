using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public sealed class SafeJSObjectReference(SafeJSRuntime safeJSRuntime, IJSObjectReference source)
    : IJSObjectReference, IHasDisposeStatus
{
    private volatile int _isDisposed;

    public SafeJSRuntime SafeJSRuntime { get; } = safeJSRuntime;
    public IJSObjectReference Source { get; } = source;
    public bool IsDisposed => _isDisposed != 0;

    public ValueTask DisposeAsync()
        => Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0 || SafeJSRuntime.IsDisconnected
            ? default
            : Source.DisposeSilentlyAsync();

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(SafeJSRuntime.JsonSerialized)] TValue>(
        string identifier, object?[]? args)
    {
        ThrowIfDisposed();
        SafeJSRuntime.RequireConnected();
        return Source.InvokeAsync<TValue>(identifier, SafeJSRuntime.ToUnsafe(args));
    }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(SafeJSRuntime.JsonSerialized)] TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        ThrowIfDisposed();
        SafeJSRuntime.RequireConnected();
        return Source.InvokeAsync<TValue>(identifier, cancellationToken, SafeJSRuntime.ToUnsafe(args));
    }

    // Protected methods

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(IsDisposed, this);
}
