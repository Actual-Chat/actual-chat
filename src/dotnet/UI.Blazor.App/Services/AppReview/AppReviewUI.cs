using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Takes the user to a place where they can rate the app: the native in-app review flow where
/// an <see cref="IAppReviewer"/> is registered, the store's write-review page otherwise.
/// </summary>
public sealed class AppReviewUI(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private readonly IAppReviewer? _reviewer = hub.Services.GetService<IAppReviewer>();

    private string? ReviewUrl => Links.Apps.Review(HostInfo.AppKind);

    public bool IsAvailable => HostInfo.HostKind.IsMauiApp() && ReviewUrl != null;

    public async Task<AppReviewOutcome> LeaveReview(CancellationToken cancellationToken = default)
    {
        var outcome = await RequestNativeReview(cancellationToken).ConfigureAwait(false);
        if (outcome != AppReviewOutcome.Failed)
            return outcome;
        if (ReviewUrl is not { } reviewUrl)
            return AppReviewOutcome.Failed;

        await Hub.ExternalUrlOpener.Open(reviewUrl).ConfigureAwait(false);
        return AppReviewOutcome.Requested;
    }

    // Private methods

    private async Task<AppReviewOutcome> RequestNativeReview(CancellationToken cancellationToken)
    {
        if (_reviewer is null)
            return AppReviewOutcome.Failed;

        try {
            return await _reviewer.RequestReview(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Native review request failed, falling back to the store link");
            return AppReviewOutcome.Failed;
        }
    }
}
