using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop.Implementation;

namespace ActualChat.UI.Blazor;

/// <summary>
/// Workaround for blazor server after <seealso href="https://github.com/dotnet/aspnetcore/pull/32901"/>
/// Mutes only <see cref="Microsoft.JSInterop.JSDisconnectedException" />
/// </summary>
public static class JSObjectReferenceExt
{
    private static readonly Func<JSObjectReference, JSRuntime> _jsRuntimeGetter = typeof(JSObjectReference)
        .GetField("_jsRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetGetter<JSObjectReference, JSRuntime>();

    public static JSRuntime? GetJSRuntime(this IJSObjectReference jsRef)
        => jsRef is JSObjectReference typedJSRef
            ? _jsRuntimeGetter.Invoke(typedJSRef)
            : null;

    public static bool IsDisconnected(this IJSObjectReference jsRef)
        => jsRef.GetJSRuntime()?.IsDisconnected() ?? false;

    public static IJSObjectReference? IfConnected(IJSObjectReference jsRef)
        => jsRef.IsDisconnected() ? null : jsRef;

    public static IJSObjectReference RequireConnected(IJSObjectReference jsRef)
        => jsRef.IsDisconnected() ? throw JSRuntimeErrors.Disconnected() : jsRef;

    public static ValueTask DisposeSilentlyAsync(this IJSObjectReference? jsRef, string jsDisposeMethodName = "")
    {
        return ReferenceEquals(jsRef, null)
            ? default
            : DisposeSilentlyAsyncImpl(jsRef, jsDisposeMethodName);

        async ValueTask DisposeSilentlyAsyncImpl(IJSObjectReference jsRef1, string jsDisposeMethodName1)
        {
            var js = jsRef.GetJSRuntime();
            if (js?.IsDisconnected() == true)
                return;

            try {
                if (!jsDisposeMethodName1.IsNullOrEmpty())
                    try {
                        await jsRef1.InvokeVoidAsync(jsDisposeMethodName1);
                    }
                    catch (OperationCanceledException) { }
                    catch (JSDisconnectedException) { }
                    catch (Exception) when (js?.IsDisconnected() == true) { }
            }
            finally {
                try {
                    await jsRef1.DisposeAsync();
                }
                catch (OperationCanceledException) { }
                catch (JSDisconnectedException) { }
                catch (Exception) when (js?.IsDisconnected() == true) { }
            }
        }
    }
}
