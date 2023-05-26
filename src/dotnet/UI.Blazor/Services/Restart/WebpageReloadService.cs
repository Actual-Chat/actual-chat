namespace ActualChat.UI.Blazor.Services;

public sealed class WebpageReloadService : IRestartService
{
    private ILogger Log { get; }
    private NavigationManager Nav { get; }

    public WebpageReloadService(IServiceProvider services)
    {
        Log = services.LogFor<WebpageReloadService>();
        Nav = services.GetRequiredService<NavigationManager>();
    }

    public void Restart()
    {
        Log.LogInformation("About to Restart");
        Nav.NavigateTo(Links.Home, true);
    }
}
