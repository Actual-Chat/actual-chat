namespace ActualChat.UI.Blazor.App.Services;

// A UIActionFailureTracker that localizes each failure's message before publishing it: the failure
// is added (and listeners notified via Changed) only once translation is ready, or after a timeout
// that falls back to the original English text. Registered in DI in place of the base tracker, so
// ToastHost (and any other consumer) surfaces localized errors without changes.

public sealed class LocalizingUIActionFailureTracker : UIActionFailureTracker
{
    private static readonly TimeSpan LocalizeTimeout = TimeSpan.FromSeconds(10);

    private LocalizationUI LocalizationUI => field ??= Services.GetRequiredService<LocalizationUI>();

    public LocalizingUIActionFailureTracker(Options settings, IServiceProvider services)
        : base(settings, services, mustStart: false)
        => Start();

    protected override bool TryAddFailure(IUIActionResult? result)
    {
        if (result is null || !result.HasError)
            return false;

        _ = AddLocalizedFailure(result);
        return false;
    }

    private async Task AddLocalizedFailure(IUIActionResult result)
    {
        var error = result.Error!;
        var message = error.Message;
        try {
            using var cts = new CancellationTokenSource(LocalizeTimeout);
            message = await LocalizationUI.Get(error.Message, cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) {
            if (e is not OperationCanceledException)
                Log.LogWarning(e, "Failed to localize action failure: {Message}", error.Message);
        }
        var localized = message == error.Message
            ? result
            : new LocalizedUIActionResult(result, message);
        base.TryAddFailure(localized);
    }
}
