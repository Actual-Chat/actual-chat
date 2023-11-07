using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public class SafeJSObjectReference(SafeJSRuntime safeJSRuntime, IJSObjectReference jsObjectReference)
    : IJSObjectReference
{
    internal bool Disposed { get; set; }
    internal IJSObjectReference JSObjectReference => jsObjectReference;

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(SafeJSRuntime.JsonSerialized)] TValue>(string identifier, object?[]? args)
    {
        ThrowIfDisposed();
        safeJSRuntime.EnsureConnected();
        return jsObjectReference.InvokeAsync<TValue>(identifier, safeJSRuntime.UnwrapArgs(args));
    }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(SafeJSRuntime.JsonSerialized)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        ThrowIfDisposed();
        safeJSRuntime.EnsureConnected();
        return jsObjectReference.InvokeAsync<TValue>(identifier, cancellationToken, safeJSRuntime.UnwrapArgs(args));
    }

    public ValueTask DisposeAsync()
    {
        if (!Disposed) {
            Disposed = true;
            safeJSRuntime.EnsureConnected();
            return jsObjectReference.DisposeAsync();
        }
        return ValueTask.CompletedTask;
    }

    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Disposed, this);
}
