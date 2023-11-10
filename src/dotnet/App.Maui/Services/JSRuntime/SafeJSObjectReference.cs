using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public class SafeJSObjectReference(SafeJSRuntime safeJSRuntime, IJSObjectReference jsObjectReference)
    : IJSObjectReference
{
    private volatile int _disposed;

    internal bool Disposed => _disposed != 0;
    internal IJSObjectReference JSObjectReference => jsObjectReference;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return default;

        safeJSRuntime.RequireConnected();
        return jsObjectReference.DisposeAsync();
    }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(SafeJSRuntime.JsonSerialized)] TValue>(
        string identifier, object?[]? args)
    {
        ThrowIfDisposed();
        safeJSRuntime.RequireConnected();
        return jsObjectReference.InvokeAsync<TValue>(identifier, safeJSRuntime.UnwrapArgs(args));
    }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(SafeJSRuntime.JsonSerialized)] TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        ThrowIfDisposed();
        safeJSRuntime.RequireConnected();
        return jsObjectReference.InvokeAsync<TValue>(identifier, cancellationToken, safeJSRuntime.UnwrapArgs(args));
    }

    // Protected methods

    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Disposed, this);
}
