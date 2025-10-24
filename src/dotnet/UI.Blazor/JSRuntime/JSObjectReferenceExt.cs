namespace ActualChat.UI.Blazor;

public static class JSObjectReferenceExt
{
    public static IJSObjectReference ToLogging(this IJSObjectReference source, string name, ILogger? log)
        => log is null
            ? source
            : new LoggingJSObjectReference(source, name, log);

    public static ValueTask DisposeSilentlyAsync(this IJSObjectReference? jsRef, string jsDisposeMethodName = "")
    {
        return ReferenceEquals(jsRef, null)
            ? ValueTask.CompletedTask
            : DisposeSilentlyAsyncImpl(jsRef, jsDisposeMethodName);

        async ValueTask DisposeSilentlyAsyncImpl(IJSObjectReference jsRef1, string jsDisposeMethodName1)
        {
            if (!jsDisposeMethodName1.IsNullOrEmpty())
                try {
                    await jsRef1.InvokeVoidAsync(jsDisposeMethodName1).SilentAwait(false);
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
