namespace ActualChat.UI.Blazor.Components;

public abstract class ComputedMenuBase<TState> : ComputedStateComponent<TState>, IMenu
{
    private readonly TaskCompletionSource _whenClosedSource = TaskCompletionSourceExt.New();

    [Parameter] public string Id { get; set; } = "";
    [Parameter] public string[] Arguments { get; set; } = Array.Empty<string>();

    [CascadingParameter] public MenuHost Host { get; set; } = null!;

    public Task WhenClosed => _whenClosedSource.Task;

    protected override async Task OnAfterRenderAsync(bool firstRender) {
        if (!State.Snapshot.IsInitial)
            await Host.Position(this);
    }

    public override async ValueTask DisposeAsync() {
        await base.DisposeAsync();
        _whenClosedSource.TrySetResult();
    }

    protected async Task NavigateTo(string url) {
        await WhenClosed;
        await Host.History.NavigateTo(url);
    }
}
