namespace ActualChat.App.Maui;

public class MauiBlazorApp : UI.Blazor.App.App
{
    [Inject] private IServiceProvider Services { get; init; } = null!;

    protected override void OnInitialized()
    {
        ScopedServiceLocator.Initialize(Services);
        base.OnInitialized();
    }
}
