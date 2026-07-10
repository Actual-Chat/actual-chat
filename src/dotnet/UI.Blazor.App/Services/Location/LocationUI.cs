namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Public API for the local user's location sharing: forwards start/stop to <see cref="LiveLocationReporter"/>
/// (which owns the shares state) and posts one-shot current-location messages.
/// </summary>
public class LocationUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private LiveLocationReporter Reporter => field ??= Hub.Services.GetRequiredService<LiveLocationReporter>();

    public IState<GeoTrackingError?> TrackingError => Tracker.Error;

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<Avatar>> ListAvatars(ChatId chatId, CancellationToken cancellationToken)
    {
        var locations = await Hub.SharedLocations.ListLive(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (locations.Count == 0)
            return [];

        var avatars = new List<Avatar>(locations.Count);
        foreach (var location in locations) {
            var author = await Hub.Authors.Get(Session, chatId, location.AuthorId, cancellationToken).ConfigureAwait(false);
            if (author != null)
                avatars.Add(author.Avatar);
        }
        return avatars;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsLiveShare(ChatId chatId, SharedLocationId id, CancellationToken cancellationToken)
    {
        // Duration is immutable, so capture Get in isolation — IsLiveShare takes no dependency on it
        // and won't recompute on the per-fix updates that invalidate Get.
        Computed<SharedLocation?> cLocation;
        using (Computed.BeginIsolation())
            cLocation = await Computed
                .Capture(() => Hub.SharedLocations.Get(Session, chatId, id, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        return cLocation.Value is { Duration.Ticks: > 0 };
    }

    public string GetOwnSharingRemainingText(ChatId chatId, CancellationToken cancellationToken)
    {
        // TODO: implement and use from location banner and location modal
        throw new NotImplementedException();
    }

    public Task StartSharing(ChatId chatId, TimeSpan duration, CancellationToken cancellationToken)
        => Reporter.StartSharing(chatId, duration, cancellationToken);

    public Task StopSharing(ChatId chatId, CancellationToken cancellationToken)
        => Reporter.StopSharing(chatId, cancellationToken);

    public async Task SendCurrentLocation(ChatId chatId, CancellationToken cancellationToken)
    {
        if (await Tracker.Get(cancellationToken).ConfigureAwait(false) is not { } point)
            return;

        var change = Change.Create(new SharedLocationDiff { Point = point, LiveDuration = TimeSpan.Zero });
        var shared = await Commander.Call(
                new SharedLocations_Change(Session, chatId, null, change),
                cancellationToken)
            .ConfigureAwait(false);
        if (shared is null)
            return;

        var command = new Chats_UpsertEntry(Session, chatId, null) { LocationId = shared.Id };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }
}
