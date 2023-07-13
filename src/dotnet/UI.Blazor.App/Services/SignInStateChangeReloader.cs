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
        var session = Services.Session();
        var account = Services.GetRequiredService<IAccounts>();

        var updateDelayer = FixedDelayer.Instant;
        var cAccount = (Computed<AccountFull>?)null;
        while (true) {
            try {
                if (cAccount == null) {
                    cAccount = await Computed
                        .Capture(() => account.GetOwn(session, cancellationToken))
                        .ConfigureAwait(false);
                    // Wait for non-cached account retrieval
                    cAccount = await cAccount.UpdateIfCached(cancellationToken).ConfigureAwait(false);
                    // And wait till the moment all errors are gone
                    cAccount = await cAccount.When((_, error) => error == null, cancellationToken).ConfigureAwait(false);
                }

                // Wait for sign-in unless already signed in
                if (cAccount.Value.IsGuestOrNone) {
                    cAccount = await cAccount
                        .When((x, error) => error == null && !x.IsGuestOrNone, updateDelayer, cancellationToken)
                        .ConfigureAwait(false);

                    _ = History.Dispatcher.InvokeAsync(() => {
                        var currentUrl = History.LocalUrl;
                        var targetUrl = currentUrl.IsChatOrChatRoot() ? currentUrl : Links.Chats;
                        Log.LogInformation("Forced reload on sign-in: {TargetUrl}", targetUrl);
                        History.ForceReload(targetUrl);
                    });
                }
                else {
                    // Wait for sign-out
                    cAccount = await cAccount
                        .When((x, error) => error == null && x.IsGuestOrNone, updateDelayer, cancellationToken)
                        .ConfigureAwait(false);

                    _ = History.Dispatcher.InvokeAsync(async () => {
                        var clientComputedCache = Services.GetService<IClientComputedCache>();
                        if (clientComputedCache != null) {
                            // Clear computed cache on sign-out to evict cached account from there
                            await clientComputedCache.Clear(CancellationToken.None).ConfigureAwait(true);
                        }
                        var targetUrl = Links.Home;
                        Log.LogInformation("Forced reload on sign-out: {TargetUrl}", targetUrl);
                        History.ForceReload(targetUrl);
                    });
                }
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch {
                // Just in case: we don't want rapid iterations on errors here
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
