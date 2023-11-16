using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public sealed class SafeJSObjectReference(SafeJSRuntime safeJSRuntime, IJSObjectReference jsObjectReference)
    : IJSObjectReference, IHasIsDisposed
{
    private volatile int _isDisposed;

    internal IJSObjectReference JSObjectReference => jsObjectReference;

    public bool IsDisposed => _isDisposed != 0;

    public ValueTask DisposeAsync()
        => Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0 || safeJSRuntime.IsDisconnected
            ? default
            : jsObjectReference.DisposeSilentlyAsync();

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(SafeJSRuntime.JsonSerialized)] TValue>(
        string identifier, object?[]? args)
    {
        ThrowIfDisposed();
        safeJSRuntime.RequireConnected();
        return jsObjectReference.InvokeAsync<TValue>(identifier, safeJSRuntime.ToUnsafe(args));
    }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(SafeJSRuntime.JsonSerialized)] TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        ThrowIfDisposed();
        safeJSRuntime.RequireConnected();
        return jsObjectReference.InvokeAsync<TValue>(identifier, cancellationToken, safeJSRuntime.ToUnsafe(args));
    }

    // Protected methods

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(IsDisposed, this);
}
