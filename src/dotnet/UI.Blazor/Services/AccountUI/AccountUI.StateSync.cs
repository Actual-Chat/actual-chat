using Stl.Fusion.Client.Caching;

namespace ActualChat.UI.Blazor.Services;

public partial class AccountUI
{
    // All state sync logic should be here

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new AsyncChain[] {
            new(nameof(Update), Update),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task Update(CancellationToken cancellationToken)
    {
        var cOwnAccount = await Computed
            .Capture(() => Accounts.GetOwn(Session, cancellationToken))
            .ConfigureAwait(false);
        cOwnAccount = await cOwnAccount.UpdateIfCached(TimeSpan.FromSeconds(2), cancellationToken);
        var changes = cOwnAccount.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var (newAccount, error) in changes.ConfigureAwait(false)) {
            if (error != null || newAccount == null!)
                continue;

            var oldAccount = OwnAccount.Value;
            if (ReferenceEquals(oldAccount, newAccount))
                continue;

            Log.LogInformation("Update: new account: {Account}", newAccount);
            _ownAccount.Value = newAccount;
            if (_whenLoadedSource.TrySetResult())
                continue; // It's an initial account change

            if (oldAccount.Id == newAccount.Id)
                continue; // Only account properties have changed

            await BlazorCircuitContext.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            await BlazorCircuitContext.Dispatcher.InvokeAsync(OnAccountChange).ConfigureAwait(false);
        }
    }

    // Private methods

    private async Task OnAccountChange()
    {
        var account = OwnAccount.Value;
        var isSignIn = !account.IsGuestOrNone;

        for (var i = 0; i < 3; i++) {
            try {
                var history = Services.GetRequiredService<History>();
                var targetUrl = history.LocalUrl;
                if (isSignIn) { // Sign-in or account change
                    if (!targetUrl.IsChatOrChatRoot())
                        targetUrl = Links.Chats;
                }
                else { // Sign-out
                    targetUrl = Links.Home;
                    // Clear computed cache on sign-out to evict cached account from there
                    var clientComputedCache = Services.GetService<IClientComputedCache>();
                    if (clientComputedCache != null)
                        await clientComputedCache.Clear(CancellationToken.None).ConfigureAwait(true);
                }

                await history.ForceReload(isSignIn ? "sign-in" : "sign-out", targetUrl);
                return;
            }
            catch (InvalidOperationException) {
                // History may fail due to uninitialized NavigationManager, we'll retry in this case
                await Task.Delay(100);
            }
        }
    }
}
