namespace ActualChat.UI.Blazor.Components;

public abstract class MenuBase : ComponentBase, IMenu, IDisposable
{
    private readonly TaskCompletionSource _whenClosedSource = TaskCompletionSourceExt.New();

    [Parameter] public string Id { get; set; } = "";
    [Parameter] public string[] Arguments { get; set; } = Array.Empty<string>();

    [CascadingParameter] public MenuHost Host { get; set; } = null!;

    public Task WhenClosed => _whenClosedSource.Task;

    protected override async Task OnAfterRenderAsync(bool firstRender)
        => await Host.Position(this);

    public void Dispose()
        => _whenClosedSource.TrySetResult();

    protected async Task NavigateTo(string url) {
        await WhenClosed;
        await Host.History.NavigateTo(url);
    }
}
