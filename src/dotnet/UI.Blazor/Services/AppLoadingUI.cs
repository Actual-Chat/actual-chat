namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Allows to delay splash screen removal in MAUI app.
/// </summary>
public sealed class AppLoadingUI
{
    private readonly TaskSource<Unit> _whenLoadedSource;

    public Task WhenLoaded => _whenLoadedSource.Task;

    public AppLoadingUI()
        => _whenLoadedSource = TaskSource.New<Unit>(true);

    public void Loaded()
        => _whenLoadedSource.TrySetResult(default);
}
