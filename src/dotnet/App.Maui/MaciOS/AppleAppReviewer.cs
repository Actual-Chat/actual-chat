using ActualChat.UI.Blazor.Services;
using Foundation;
using StoreKit;

namespace ActualChat.App.Maui;

public sealed class AppleAppReviewer : IAppReviewer
{
    // StoreKit denies the prompt for every TestFlight build (itunesstored: "isBeta: YES ...
    // Review request denied") and never tells the app; the sandbox receipt is what marks one.
#pragma warning disable CA1422
    private static bool IsBetaBuild
        => NSBundle.MainBundle.AppStoreReceiptUrl?.LastPathComponent == "sandboxReceipt";
#pragma warning restore CA1422

    public Task<AppReviewOutcome> RequestReview(CancellationToken cancellationToken)
        => MainThread.InvokeOnMainThreadAsync(() => {
            if (IsBetaBuild)
                return AppReviewOutcome.Failed;
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
