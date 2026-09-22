namespace ActualChat.UI.Blazor.Services;

public static class UITextLocalizerExt
{
    extension(IUITextLocalizer localizer)
    {
        public async Task<string> GetSafe(string message, CancellationToken cancellationToken = default)
        {
            // Deliberately outside Get, which is a compute method: its error is what Fusion caches,
            // and ComputedTransiencyResolver retries that in 1s. Get swallows just the offline
            // connect timeout, where it also depends on connectivity to re-translate on reconnect.
            try {
                return await localizer.Get(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                localizer.Services.LogFor(localizer.GetType().NonProxyType())
                    .LogError(e, "Failed to localize: {Message}", message);
                return message;
            }
        }
    }
}
