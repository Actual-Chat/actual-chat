using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

// When the page gets reloaded in MAUI app (e.g. due to "Restart" button press there):
// - Blazor.start triggers creation of a new service provider scope for the new page
// - The old scope is disposed, but this might take a while (it's an async disposal)
// - And its IJSRuntime actually continues to work during the disposal -
//   even though JS and element references no longer work there,
//   it can still call JS - but FROM THE NEWLY LOADED PAGE!
//
// The consequences of this are:
// - ActualChat.UI.Blazor.JSObjectReferenceExt.DisposeSilentlyAsync calls may
//   fail with 'JS object instance with ID xxx does not exist'
// - Any invocations of JS methods which don't require JSObjectRef / DotNetObjectRef
//   still continue to work (e.g., think JS methods).
//
// So to address this, we:
// - Manually tag disconnected runtimes via MarkDisconnected
// - Suppress all exceptions that happen due to runtime disconnection
//   in DisposeSilentlyAsync
// - Manually throw JSRuntimeDisconnected from SafeJSRuntime, if it
//   wraps a disconnected JS runtime.
public sealed class SafeJSRuntime(IJSRuntime webViewJSRuntime) : IJSRuntime
{
    internal const DynamicallyAccessedMemberTypes JsonSerialized =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties;

    private volatile int _state; // 1 = ready, 2 = disconnected

    public bool IsDisconnected => _state == 2;
    public bool IsReady => _state != 0;

    internal IJSRuntime WebViewJSRuntime { get; } = webViewJSRuntime;

    internal bool MarkReady()
        => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

    internal void MarkDisconnected()
        => Interlocked.Exchange(ref _state, 2);

    public async ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(
        string identifier, object?[]? args)
    {
        try {
            var result = await RequireConnected().InvokeAsync<TValue>(identifier, UnwrapArgs(args)).ConfigureAwait(false);
            return Map(result);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (JSDisconnectedException) {
            throw;
        }
        catch (Exception e) {
            if (e is not JSDisconnectedException && IsDisconnected)
                throw JSRuntimeErrors.Disconnected(e);
            throw;
        }
    }

    public async ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        try {
            var result = await RequireConnected().InvokeAsync<TValue>(identifier, cancellationToken, UnwrapArgs(args)).ConfigureAwait(false);
            return Map(result);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (JSDisconnectedException) {
            throw;
        }
        catch (Exception e) {
            if (e is not JSDisconnectedException && IsDisconnected)
                throw JSRuntimeErrors.Disconnected(e);
            throw;
        }
    }

    internal IJSRuntime RequireConnected()
        => IsDisconnected
            ? throw JSRuntimeErrors.Disconnected()
            : WebViewJSRuntime;

    internal object?[]? UnwrapArgs(object?[]? args)
    {
        if (args == null || args.Length == 0)
            return args;

        var needUnwrap = args.Any(static c => c is SafeJSObjectReference);
        if (!needUnwrap)
            return args;

        var newArgs = new object?[args.Length];
        for (int i = 0; i < args.Length; i++)
            newArgs[i] = UnwrapArg(args[i]);
        return newArgs;
    }

    private object? UnwrapArg(object? obj)
        => obj is SafeJSObjectReference safeJSObjectReference
            ? safeJSObjectReference.JSObjectReference
            : obj;

    private TValue Map<TValue>(TValue value)
    {
        if (value is IJSObjectReference jsObjectReference)
            // Convert original JSObjectReference to SafeJSObjectReference to make
            // calling IJSObjectReference.InvokeAsync respect SafeJSRuntime disconnected state.
            return (TValue)(object)new SafeJSObjectReference(this, jsObjectReference);
        return value;
    }
}
