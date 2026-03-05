using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

// TODO(DF)(FC): Get rid of it
public class MauiSentryInitializer(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    private static readonly TimeSpan SentryStartDelay = TimeSpan.FromSeconds(10);

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(Initialize).LogError(Log).RunIsolated(cancellationToken);

    private async Task Initialize(CancellationToken cancellationToken)
    {
        await LoadingUI.WhenAppRendered.WithDelay(SentryStartDelay, cancellationToken).ConfigureAwait(false);
        MauiDiagnostics.InitSentrySdk();
        await CreateSentryTraceProvider().ConfigureAwait(false);
    }

    private static async Task CreateSentryTraceProvider()
    {
        // Initialize client trace provider only in the development environment or for admin users.
 #pragma warning disable CS0162 // Unreachable code detected
        if (!MauiSettings.IsDevApp) {
            var scopedServices = await WhenBlazorAppServicesReady().ConfigureAwait(false);
            var accountUI = scopedServices.GetRequiredService<AccountUI>();
            await accountUI.WhenReady.ConfigureAwait(false);
            var ownAccount = await accountUI.OwnAccount.Use().ConfigureAwait(false);
            if (!ownAccount.IsAdmin)
                return;
        }
 #pragma warning restore CS0162 // Unreachable code detected
        MauiDiagnostics.CreateSentryTraceProvider();
    }
}
