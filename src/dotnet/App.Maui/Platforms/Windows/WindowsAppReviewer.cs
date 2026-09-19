using ActualChat.UI.Blazor.Services;
using Windows.Services.Store;
using WinRT.Interop;
using Application = Microsoft.Maui.Controls.Application;
using Window = Microsoft.UI.Xaml.Window;

namespace ActualChat.App.Maui;

public sealed class WindowsAppReviewer : IAppReviewer
{
    public Task<AppReviewOutcome> RequestReview(CancellationToken cancellationToken)
        => MainThread.InvokeOnMainThreadAsync(async () => {
            if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Window window)
                return AppReviewOutcome.Failed;

            // GetDefault throws in an unpackaged build; AppReviewUI turns that into the store link
            var storeContext = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(storeContext, WindowNative.GetWindowHandle(window));
            var result = await storeContext.RequestRateAndReviewAppAsync().ConfigureAwait(false);
            return result.Status switch {
                StoreRateAndReviewStatus.Succeeded => AppReviewOutcome.Completed,
                StoreRateAndReviewStatus.CanceledByUser => AppReviewOutcome.Cancelled,
                _ => AppReviewOutcome.Failed,
            };
        });
}
