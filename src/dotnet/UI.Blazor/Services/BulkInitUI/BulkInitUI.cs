namespace ActualChat.UI.Blazor.Services;

public class BulkInitUI
{
    private IJSRuntime JS { get; }

    public BulkInitUI(IJSRuntime js)
        => JS = js;

    public async ValueTask Invoke(Func<List<object?>, ValueTask> initializer)
    {
        var calls = new List<object?>();
        await initializer.Invoke(calls).ConfigureAwait(false);
        await JS.InvokeVoidAsync("window.App.bulkInit", calls.ToArray()).ConfigureAwait(false);
    }
}
