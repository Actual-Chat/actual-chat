namespace ActualChat.UI.Blazor.Services;

public class BrowserInit
{
    private IJSRuntime JS { get; }

    public BrowserInit(IJSRuntime js)
        => JS = js;

    public async ValueTask Initialize(string? sessionHash, Func<List<object?>, ValueTask> callsBuilder)
    {
        var calls = new List<object?>();
        await callsBuilder.Invoke(calls).ConfigureAwait(false);
        await JS.InvokeVoidAsync("window.App.browserInit", sessionHash, calls.ToArray()).ConfigureAwait(false);
    }
}
