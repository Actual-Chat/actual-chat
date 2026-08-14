namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Whether iOS Live Activities (ActivityKit) are available and enabled for the app.
/// On non-iOS platforms, implementations should return <c>true</c> so platform-neutral
/// banners/checks aren't triggered.
/// </summary>
public interface ILiveActivitiesAvailability
{
    Task<bool> IsEnabled(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default availability for non-iOS platforms.
/// </summary>
public sealed class DefaultLiveActivitiesAvailability : ILiveActivitiesAvailability
{
    public Task<bool> IsEnabled(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
