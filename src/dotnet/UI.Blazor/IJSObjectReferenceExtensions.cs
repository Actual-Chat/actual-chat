using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor;

public static class JSObjectReferenceExt
{
    /// <summary>
    /// Workaround for blazor server after <seealso href="https://github.com/dotnet/aspnetcore/pull/32901"/>
    /// Mutes only <see cref="Microsoft.JSInterop.JSDisconnectedException" />
    /// </summary>
    public static async ValueTask DisposeSilentAsync(this IJSObjectReference jsRef)
    {
        try {
            await jsRef.DisposeAsync().ConfigureAwait(true);
        }
        catch (JSDisconnectedException) { }
    }
}
