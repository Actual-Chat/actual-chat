namespace ActualChat.UI.Blazor.Services;

public class ReloadUI
{
    private IServiceProvider Services { get; }

    public ReloadUI(IServiceProvider services)
        => Services = services;

    public void Reload(string? url = null)
    {
        var dispatcher = Services.GetRequiredService<Dispatcher>();
        _ = dispatcher.InvokeAsync(() => {
            var nav = Services.GetService<NavigationManager>();
            url ??= nav?.Uri;
            var log = Services.LogFor(GetType());
            log.LogWarning("Reloading URL: {Url}", url);
            if (nav == null) {
                log.LogError("Can't reload: NavigationManager!");
                return;
            }
            nav.NavigateTo(url ?? Links.Home, true);
        });
    }
}
