namespace ActualChat.UI.Blazor.App.Services;

public class SignOutReloader : WorkerBase
{
    private IServiceProvider Services { get; }

    public SignOutReloader(IServiceProvider services)
        => Services = services;

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var session = Services.GetRequiredService<Session>();
        var auth = Services.GetRequiredService<IAuth>();
        var cAuthInfo = await Computed.Capture(
            ct => auth.GetAuthInfo(session, ct), cancellationToken).ConfigureAwait(false);

        var wasAuthenticated = false;
        await foreach (var c in cAuthInfo.Changes(cancellationToken).ConfigureAwait(false)) {
            if (!c.IsValue(out var authInfo))
                continue;
            var isAuthenticated = authInfo?.IsAuthenticated() ?? false;
            if (!wasAuthenticated) {
                wasAuthenticated |= isAuthenticated;
                continue;
            }
            if (!isAuthenticated)
                break;
        }

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.Uri, true);
    }
}
