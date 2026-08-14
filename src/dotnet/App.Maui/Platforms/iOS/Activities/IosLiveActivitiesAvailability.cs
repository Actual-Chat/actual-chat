using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Activities;

/// <summary>
/// iOS Live Activities availability check via ActivityKit's
/// <c>ActivityAuthorizationInfo().areActivitiesEnabled</c>.
/// </summary>
public sealed class IosLiveActivitiesAvailability : ILiveActivitiesAvailability
{
    public Task<bool> IsEnabled(CancellationToken cancellationToken = default)
        => Task.FromResult(IosLiveActivities.AreEnabled() == 1);
}
