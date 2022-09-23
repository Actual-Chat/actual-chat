namespace ActualChat.UI.Blazor;

/// <summary>
/// Workaround for blazor server after <seealso href="https://github.com/dotnet/aspnetcore/pull/32901"/>
/// Mutes only <see cref="Microsoft.JSInterop.JSDisconnectedException" />
/// </summary>
public static class JSObjectReferenceExt
{
    public static ValueTask DisposeSilentlyAsync(this IJSObjectReference? jsObjectRef, string jsDisposeMethodName = "")
    {
        return jsObjectRef == null
            ? ValueTask.CompletedTask
            : DisposeSilentlyAsyncImpl(jsObjectRef, jsDisposeMethodName);

        async ValueTask DisposeSilentlyAsyncImpl(IJSObjectReference jsObjectRef1, string jsDisposeMethodName1)
        {
            if (!jsDisposeMethodName1.IsNullOrEmpty())
                try {
                    await jsObjectRef1.InvokeVoidAsync(jsDisposeMethodName1);
                }
                catch (OperationCanceledException) { }
                catch (JSDisconnectedException) { }

            try {
                await jsObjectRef1.DisposeAsync();
            }
            catch (OperationCanceledException) { }
            catch (JSDisconnectedException) { }
        }
    }
}
