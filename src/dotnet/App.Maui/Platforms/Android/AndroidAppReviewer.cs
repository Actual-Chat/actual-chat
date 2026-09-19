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
        // Play decides whether the card actually appears (quota-limited), and never says.
        var reviewInfo = await reviewManager.RequestReviewFlow().AsAsync<ReviewInfo>().ConfigureAwait(false);
        await MainThread.InvokeOnMainThreadAsync(
            () => reviewManager.LaunchReviewFlow(activity, reviewInfo).AsAsync()).ConfigureAwait(false);
        return AppReviewOutcome.Requested;
    }
}
