using ActualChat.UI.Blazor.Services;
using Android.Gms.Extensions;
using Xamarin.Google.Android.Play.Core.Review;

namespace ActualChat.App.Maui;

public sealed class AndroidAppReviewer : IAppReviewer
{
    public async Task<AppReviewOutcome> RequestReview(CancellationToken cancellationToken)
    {
        var activity = MainActivity.Current;
        var reviewManager = ReviewManagerFactory.Create(activity);
        // Throws for a build not installed from Play; AppReviewUI turns that into the store link.
        var reviewInfo = await reviewManager.RequestReviewFlow().AsAsync<ReviewInfo>().ConfigureAwait(false);
        // Play declines the card silently (quota spent, account or build not eligible) by answering
        // with a no-op ReviewInfo: LaunchReviewFlow then completes at once without starting anything.
        // The flag isn't exposed, but ReviewInfo is an AutoValue type whose ToString() prints it.
        if (reviewInfo.ToString()?.Contains("isNoOp=true", StringComparison.Ordinal) == true)
            return AppReviewOutcome.Failed;

        // Play still decides whether the card actually appears, and never says.
        await MainThread.InvokeOnMainThreadAsync(
            () => reviewManager.LaunchReviewFlow(activity, reviewInfo).AsAsync()).ConfigureAwait(false);
        return AppReviewOutcome.Requested;
    }
}
