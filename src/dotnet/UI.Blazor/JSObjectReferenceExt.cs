using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor;

/// <summary>
/// Workaround for blazor server after <seealso href="https://github.com/dotnet/aspnetcore/pull/32901"/>
/// Mutes only <see cref="Microsoft.JSInterop.JSDisconnectedException" />
/// </summary>
public static class JSObjectReferenceExt
{
    public static ValueTask DisposeSilentAsync(this IJSObjectReference? jsObjectRef)
        => jsObjectRef == null
            ? ValueTask.CompletedTask
            : jsObjectRef.DisposeAsync().Suppress<JSDisconnectedException>();

    public static async ValueTask DisposeSilentAsync(this IJSObjectReference? jsObjectRef, string jsMethodName)
    {
        if (jsObjectRef == null)
            return;
        await jsObjectRef.InvokeVoidAsync(jsMethodName).Suppress<JSDisconnectedException>().ConfigureAwait(true);
        await jsObjectRef.DisposeAsync().Suppress<JSDisconnectedException>();
    }
}
