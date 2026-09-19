namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Opens the platform's native in-app rating flow. Implemented only where one exists;
/// where it's absent or fails, the caller falls back to the store's write-review page.
/// </summary>
public interface IAppReviewer
{
    Task<AppReviewOutcome> RequestReview(CancellationToken cancellationToken);
}

public enum AppReviewOutcome
{
    // The OS was asked; whether it showed anything is unknowable (iOS, Android)
    Requested,
    // Only Windows reports these two
    Completed,
    Cancelled,
    // The native path is unusable here, e.g. a build not installed from the store
    Failed,
}
