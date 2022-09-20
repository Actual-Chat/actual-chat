namespace ActualChat.UI.Blazor.App.Services;

public class SignOutReloader : WorkerBase
{
    private IServiceProvider Services { get; }
    private ILogger Log { get; }

    public SignOutReloader(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var session = Services.GetRequiredService<Session>();
        var auth = Services.GetRequiredService<IAuth>();
        var cAuthInfo0 = await Computed
            .Capture(() => auth.GetAuthInfo(session, cancellationToken))
            .ConfigureAwait(false);

        var updateDelayer = FixedDelayer.Get(0.5);
        while (true) {
            try {
                await cAuthInfo0.When(i => i?.IsAuthenticated() ?? false, updateDelayer, cancellationToken).ConfigureAwait(false);
                await cAuthInfo0.When(i => !(i?.IsAuthenticated() ?? false), updateDelayer, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch {
                // Intended
            }
        }

        Log.LogInformation("Forcing reload on sign-out");
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.Uri, true);
    }
}
