namespace ActualChat.UI.Blazor.Services;

public sealed class WebpageReloadService : IRestartService
{
    private NavigationManager Nav { get; }

    public WebpageReloadService(NavigationManager nav)
        => Nav = nav;

    public void Restart()
        => Nav.NavigateTo(Links.Home, true);
}
