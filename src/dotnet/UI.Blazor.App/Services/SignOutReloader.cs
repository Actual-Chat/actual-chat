using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class SignOutReloader : WorkerBase
{
    private IServiceProvider Services { get; }
    private History History { get; }
    private UICommander UICommander { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public SignOutReloader(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        History = services.GetRequiredService<History>();
        UICommander = Services.UICommander();
        Clocks = services.Clocks();
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var session = Services.GetRequiredService<Session>();
        var auth = Services.GetRequiredService<IAuth>();

        var updateDelayer = FixedDelayer.Instant;
        while (true) {
            try {
                var cAuthInfo0 = await Computed
                    .Capture(() => auth.GetAuthInfo(session, cancellationToken))
                    .ConfigureAwait(false);

                // Wait for sign-in unless already signed in
                if (cAuthInfo0.ValueOrDefault?.IsAuthenticated() != true) {
                    await cAuthInfo0.When(i => i?.IsAuthenticated() ?? false, updateDelayer, cancellationToken).ConfigureAwait(false);
                    await UICommander.RunNothing().ConfigureAwait(false); // Reset all update delays
                }

                var onboardingUI = Services.GetRequiredService<OnboardingUI>();
                _ = History.Dispatcher.InvokeAsync(() => onboardingUI.TryShow());

                // Wait for sign-out
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
        _ = History.Dispatcher.InvokeAsync(() => {
            if (History.HostInfo.AppKind.IsMauiApp()) {
                // MAUI scenario:
                // - Reset MustNavigateToChatsOnSignIn
                // - Go to home page
                var autoNavigationUI = Services.GetRequiredService<AutoNavigationUI>();
                autoNavigationUI.MustNavigateToChatsOnSignIn = true;
                History.NavigateTo(Links.Home);
                return ValueTask.CompletedTask;
            }

            // Blazor Server/WASM scenario:
            // - Hard reload of home page
            return History.HardNavigateTo(Links.Home);
        });
    }
}
