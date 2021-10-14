using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor;

/// <summary>
/// Workaround for blazor server after <seealso href="https://github.com/dotnet/aspnetcore/pull/32901"/>
/// Mutes only <see cref="Microsoft.JSInterop.JSDisconnectedException" />
/// </summary>
public static class JSObjectReferenceExt
{
    public static ValueTask DisposeSilentlyAsync(this IJSObjectReference? jsObjectRef)
        => jsObjectRef == null
            ? ValueTask.CompletedTask
            : jsObjectRef.DisposeAsync().Suppress<JSDisconnectedException>();

    public static ValueTask DisposeSilentlyAsync(this IJSObjectReference? jsObjectRef, string jsMethodName)
    {
        return jsObjectRef == null
            ? ValueTask.CompletedTask
            : DisposeSilentlyAsyncImpl(jsObjectRef, jsMethodName);

        async ValueTask DisposeSilentlyAsyncImpl(IJSObjectReference jsObjectRef1, string jsMethodName1)
        {
            await jsObjectRef1.InvokeVoidAsync(jsMethodName1).Suppress<JSDisconnectedException>().ConfigureAwait(true);
            await jsObjectRef1.DisposeAsync().Suppress<JSDisconnectedException>().ConfigureAwait(false);
        }
    }
}
