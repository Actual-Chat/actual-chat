using System.Linq.Expressions;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Implementation;

namespace ActualChat.App.Maui.Services;

// In MAUI when page is refreshed,
// js runtime is not disconnected, but element references are no longer available on a page,
// and call ActualChat.UI.Blazor.JSObjectReferenceExt.DisposeSilentlyAsync calls fail
// with an exception 'JS object instance with ID xxx does not exist'.
// So we manually mark this runtime as disconnected and do not do invokes
// inside ActualChat.UI.Blazor.JSObjectReferenceExt.DisposeSilentlyAsync.
public static class JSObjectReferenceDisconnectHelper
{
    private static readonly ConditionalWeakTable<IJSRuntime, object> _disconnectedRuntimes = new ();
    private static readonly Func<JSObjectReference, JSRuntime> _getRuntime;

    static JSObjectReferenceDisconnectHelper()
    {
        var fi = typeof(JSObjectReference).GetField("_jsRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
        var p = Expression.Parameter(typeof(JSObjectReference));
        var ma = Expression.Field(p, fi!);
        var lambda = Expression.Lambda<Func<JSObjectReference, JSRuntime>>(ma, p);
        _getRuntime = lambda.Compile();
    }

    public static bool TestIfIsDisconnected(IJSObjectReference jsRef)
    {
        var jsRef2 = jsRef as JSObjectReference;
        if (jsRef2 == null)
            return false;
        var runtime = _getRuntime(jsRef2);
        if (_disconnectedRuntimes.TryGetValue(runtime, out _))
            return true;
        return false;
    }

    public static void MarkAsDisconnected(IJSRuntime jsRuntime)
    {
        if (jsRuntime == null) throw new ArgumentNullException(nameof(jsRuntime));
        _disconnectedRuntimes.Add(jsRuntime, _disconnectedRuntimes);
    }
}
