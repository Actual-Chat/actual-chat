namespace ActualChat.UI.Blazor;

/// <summary>
/// Workaround for blazor server after <seealso href="https://github.com/dotnet/aspnetcore/pull/32901"/>
/// Mutes only <see cref="Microsoft.JSInterop.JSDisconnectedException" />
/// </summary>
public static class JSObjectReferenceExt
{
    public static ValueTask DisposeSilentlyAsync(this IJSObjectReference? jsRef, string jsDisposeMethodName = "")
    {
        return ReferenceEquals(jsRef, null)
            ? ValueTask.CompletedTask
            : DisposeSilentlyAsyncImpl(jsRef, jsDisposeMethodName);

        async ValueTask DisposeSilentlyAsyncImpl(IJSObjectReference jsRef1, string jsDisposeMethodName1)
        {
            if (!jsDisposeMethodName1.IsNullOrEmpty())
                try {
                    await jsRef1.InvokeVoidAsync(jsDisposeMethodName1).ConfigureAwait(false);
                }
                catch {
                    // Intended
                }
            try {
                await jsRef1.DisposeAsync().SilentAwait(false);
            }
            catch {
                // Intended
            }
        }
    }
}
