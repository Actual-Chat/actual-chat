using System.Diagnostics.CodeAnalysis;

namespace ActualChat.UI.Blazor.Services;

public sealed class JSRuntimeWithDisconnectGuard : IJSRuntime
{
    private static readonly ConditionalWeakTable<IJSRuntime, object> _disconnectedRuntimes = new ();
    private const DynamicallyAccessedMemberTypes JsonSerialized = DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties;
    private readonly IJSRuntime _target;

    private IJSRuntime JSRuntime {
        get {
            var isDisconnected = TestIfDisconnected(_target);
            if (isDisconnected)
                throw new JSDisconnectedException(
                    "JavaScript interop calls cannot be issued at this time. This is because the PageContext has disconnected " +
                    "and is being disposed.");
            return _target;
        }
    }

    public JSRuntimeWithDisconnectGuard(IJSRuntime target)
        => _target = target;

    public static void MarkAsDisconnected(IJSRuntime js)
    {
        if (js == null)
            throw new ArgumentNullException(nameof(js));
        _disconnectedRuntimes.Add(js, _disconnectedRuntimes);
    }

    public static bool TestIfDisconnected(IJSRuntime jsRuntime)
    {
        if (_disconnectedRuntimes.TryGetValue(jsRuntime, out _))
            return true;
        return false;
    }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier, object?[]? args)
        => JSRuntime.InvokeAsync<TValue>(identifier, args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => JSRuntime.InvokeAsync<TValue>(identifier, cancellationToken, args);
}
