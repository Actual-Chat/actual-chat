using ActualChat.UI.Blazor.Services;
using StoreKit;

namespace ActualChat.App.Maui;

public sealed class AppleAppReviewer : IAppReviewer
{
    public Task<AppReviewOutcome> RequestReview(CancellationToken cancellationToken)
        => MainThread.InvokeOnMainThreadAsync(() => {
            if (WindowStateManager.Default.GetCurrentUIWindow()?.WindowScene is not { } windowScene)
                return AppReviewOutcome.Failed;

            // Deprecated on iOS 18 in favor of a Swift-only API, so it stays the only option for .NET.
            // Apple decides whether the sheet actually appears (at most 3 times a year), and never says.
#pragma warning disable CA1422
            SKStoreReviewController.RequestReview(windowScene);
#pragma warning restore CA1422
            return AppReviewOutcome.Requested;
        });
}
