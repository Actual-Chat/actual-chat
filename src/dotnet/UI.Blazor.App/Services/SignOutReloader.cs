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
        var cAuthInfo0 = await Computed
            .Capture(() => auth.GetAuthInfo(session, cancellationToken))
            .ConfigureAwait(false);

        var wasAuthenticated = false;
        await foreach (var cAuthInfo in cAuthInfo0.Changes(cancellationToken).ConfigureAwait(false)) {
            if (!cAuthInfo.IsValue(out var authInfo))
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
