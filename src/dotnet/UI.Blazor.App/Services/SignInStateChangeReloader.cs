using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Stl.Fusion.Client.Caching;

namespace ActualChat.UI.Blazor.App.Services;

public class SignInStateChangeReloader : WorkerBase
{
    private IServiceProvider Services { get; }
    private History History { get; }
    private UICommander UICommander { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    private Moment Now => Clocks.CpuClock.Now;

    public SignInStateChangeReloader(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        History = services.GetRequiredService<History>();
        UICommander = services.UICommander();
        Clocks = services.Clocks();
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var session = Services.GetRequiredService<Session>();
        var account = Services.GetRequiredService<IAccounts>();

        var updateDelayer = FixedDelayer.Instant;
        while (true) {
            while (true) {
                try {
                    var cAccount = await Computed
                        .Capture(() => account.GetOwn(session, cancellationToken))
                        .ConfigureAwait(false);
                    cAccount = await cAccount.UpdateIfCached(cancellationToken).ConfigureAwait(false);

                    // Wait for sign-in unless already signed in
                    if (cAccount.Value.IsGuestOrNone) {
                        var signedOutAt = Now;
                        await cAccount.When(x => !x.IsGuestOrNone, updateDelayer, cancellationToken).ConfigureAwait(false);
                        if (Now - signedOutAt >= TimeSpan.FromSeconds(0.75)) {
                            // We check for delay here to make sure it's a human action
                            // rather than just a delayed update.
                            // Technically even zero delay is fine here, coz earlier we call
                            // UpdateIfCached to make sure we use non-cached account.
                            // But it makes sense to have some delay to decrease the probability
                            // of unintended redirect here - e.g. in case someone breaks
                            // UpdateIfCached.

                            var currentUrl = History.LocalUrl;
                            var targetUrl = currentUrl.IsDocs() ? Links.Chats : currentUrl;
                            await History.Dispatcher
                                .InvokeAsync(() => History.NavigateTo(targetUrl, targetUrl == currentUrl, true))
                                .ConfigureAwait(false);
                            // The code can't reach this point.
                        }

                        // Cached sign-out state is replaced with the real signed-in one
                        await UICommander.RunNothing().ConfigureAwait(false);
                    }

                    var onboardingUI = Services.GetRequiredService<OnboardingUI>();
                    _ = History.Dispatcher.InvokeAsync(() => onboardingUI.TryShow());

                    // Wait for sign-out
                    await cAccount.When(x => x.IsGuestOrNone, updateDelayer, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException) {
                    throw;
                }
                catch {
                    // Just in case: we don't want rapid iterations on errors here
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
            }

            Log.LogInformation("Forcing reload on sign-out");
            _ = History.Dispatcher.InvokeAsync(async () => {
                var clientComputedCache = Services.GetService<IClientComputedCache>();
                if (clientComputedCache != null) {
                    // Clear computed cache on sign-out to evict cached account from there
                    await clientComputedCache.Clear(CancellationToken.None).ConfigureAwait(true);
                }
                if (History.HostInfo.AppKind.IsMauiApp()) {
                    // MAUI scenario:
                    // - Go to home page (we don't handle forced sign-out here yet)
                    _ = History.NavigateTo(Links.Home);
                }
                else {
                    // Blazor Server/WASM scenario:
                    // - Reload home page to make sure new Session is picked up on forced sign-out
                    History.ForceReload(Links.Home);
                }
            });
        }
    }
}
