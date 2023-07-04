namespace ActualChat.UI.Blazor.Services;

public class BrowserInit
{
    private readonly TaskCompletionSource _whenInitializedSource = new();

    private IJSRuntime JS { get; }

    public Task WhenInitialized => _whenInitializedSource.Task;

    public BrowserInit(IJSRuntime js)
        => JS = js;

    public async ValueTask Initialize(string apiVersion, string? sessionHash, Func<List<object?>, ValueTask> callsBuilder)
    {
        try {
            var calls = new List<object?>();
            await callsBuilder.Invoke(calls).ConfigureAwait(false);
            await JS.InvokeVoidAsync("window.App.browserInit", apiVersion, sessionHash, calls.ToArray()).ConfigureAwait(false);
        }
        finally {
            _whenInitializedSource.TrySetResult();
        }
    }
}
