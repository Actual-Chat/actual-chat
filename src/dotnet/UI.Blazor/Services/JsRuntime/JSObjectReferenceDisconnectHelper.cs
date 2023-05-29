using System.Linq.Expressions;
using Microsoft.JSInterop.Implementation;

namespace ActualChat.UI.Blazor.Services;

// In MAUI when page is refreshed,
// js runtime is not disconnected, but element references are no longer available on a page,
// and ActualChat.UI.Blazor.JSObjectReferenceExt.DisposeSilentlyAsync calls fail
// with an exception 'JS object instance with ID xxx does not exist'.
// So we manually mark this runtime as disconnected and do not do invokes
// inside ActualChat.UI.Blazor.JSObjectReferenceExt.DisposeSilentlyAsync.
public static class JSObjectReferenceDisconnectHelper
{
    private static readonly Func<JSObjectReference, JSRuntime> _getJSRuntime;

    static JSObjectReferenceDisconnectHelper()
    {
        var parameter = Expression.Parameter(typeof(JSObjectReference));
        _getJSRuntime = Expression.Lambda<Func<JSObjectReference, JSRuntime>>(
            Expression.Field(
                parameter,
                typeof(JSObjectReference).GetField("_jsRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!),
            parameter
            ).Compile();
    }

    public static bool TestIfDisconnected(IJSObjectReference jsRef)
    {
        var typedJSRef = jsRef as JSObjectReference;
        if (typedJSRef == null)
            return false;

        var js = _getJSRuntime.Invoke(typedJSRef);
        return JSRuntimeWithDisconnectGuard.TestIfDisconnected(js);
    }
}
