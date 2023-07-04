using System.Diagnostics.CodeAnalysis;

namespace ActualChat.UI.Blazor.Services;

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
public sealed class SafeJSRuntime : IJSRuntime
{
    private const DynamicallyAccessedMemberTypes JsonSerialized =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties;

    private static readonly ConditionalWeakTable<IJSRuntime, object> DisconnectedRuntimes = new();

    public IJSRuntime Backend { get; }

    public SafeJSRuntime(IJSRuntime backend)
    {
        while (backend is SafeJSRuntime safeJSRuntime)
            backend = safeJSRuntime.Backend; // Unwrap
        Backend = backend;
    }

    public static void MarkDisconnected(IJSRuntime js)
    {
        if (js == null)
            throw new ArgumentNullException(nameof(js));
        if (js is SafeJSRuntime safeJSRuntime)
            js = safeJSRuntime.Backend;

        DisconnectedRuntimes.Add(js, DisconnectedRuntimes);
    }

    public static bool IsDisconnected(IJSRuntime js)
        => js is SafeJSRuntime safeJSRuntime
            ? safeJSRuntime.IsDisconnected()
            : DisconnectedRuntimes.TryGetValue(js, out _);

    public bool IsDisconnected()
        => DisconnectedRuntimes.TryGetValue(Backend, out _);

    public IJSRuntime? IfConnected()
        => IsDisconnected() ? null : Backend;

    public IJSRuntime RequireConnected()
        => IsDisconnected() ? throw JSRuntimeErrors.Disconnected() : Backend;

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(
        string identifier, object?[]? args)
        => RequireConnected().InvokeAsync<TValue>(identifier, args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args)
        => RequireConnected().InvokeAsync<TValue>(identifier, cancellationToken, args);
}
