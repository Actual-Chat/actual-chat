using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Public API for the local user's location sharing: forwards start/stop to <see cref="LiveLocationReporter"/>
/// (which owns the shares state) and posts one-shot current-location messages.
/// </summary>
public class LocationUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private static readonly TimeSpan RemainingTextUpdatePeriod = TimeSpan.FromSeconds(60);

    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private LiveLocationReporter Reporter => field ??= Hub.Services.GetRequiredService<LiveLocationReporter>();
    private IAuthors Authors => Hub.Authors;
    private ISharedLocations SharedLocations => Hub.SharedLocations;
    private LiveTime LiveTime => Hub.LiveTime;

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<Avatar>> ListAvatars(ChatId chatId, CancellationToken cancellationToken)
    {
        var locations = await SharedLocations.ListLive(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (locations.Count == 0)
            return [];

        var avatars = new List<Avatar>(locations.Count);
        foreach (var location in locations) {
            var author = await Authors.Get(Session, chatId, location.AuthorId, cancellationToken)
                .ConfigureAwait(false);
            if (author != null)
                avatars.Add(author.Avatar);
        }
        return avatars;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsLive(ChatId chatId, SharedLocationId id, CancellationToken cancellationToken)
    {
        // Duration is immutable, so capture Get in isolation — IsLiveShare takes no dependency on it
        // and won't recompute on the per-fix updates that invalidate Get.
        Computed<SharedLocation?> cLocation;
        using (Computed.BeginIsolation())
            cLocation = await Computed
                .Capture(() => SharedLocations.Get(Session, chatId, id, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        return cLocation.Value is { Duration.Ticks: > 0 };
    }

    [ComputeMethod]
    public virtual async Task<string> GetOwnTimeLeftText(ChatId chatId, CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        return ownAuthor is null
            ? ""
            : await GetTimeLeftText(ownAuthor.Id, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<string> GetTimeLeftText(
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var location = await GetLive(authorId, cancellationToken).ConfigureAwait(false);
        return location is null
            ? ""
            : await LiveTime.GetRemainingText(location.LiveUntil, RemainingTextUpdatePeriod, cancellationToken)
                .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<SharedLocation?> GetOwnLive(ChatId chatId, CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        return ownAuthor is null
            ? null
            : await GetLive(ownAuthor.Id, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<SharedLocation?> GetLive(
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var locations = await SharedLocations.ListLive(Session, authorId.ChatId, cancellationToken).ConfigureAwait(false);
        return locations.FirstOrDefault(x => x.AuthorId == authorId);
    }

    [ComputeMethod]
    public virtual async Task<GeoTrackingError?> GetTrackingError(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (ownAuthor is null)
            return null;

        var ownLive = await GetOwnLive(chatId, cancellationToken).ConfigureAwait(false);
        if (ownLive is null)
            return null;

        return await Tracker.Error.Use(cancellationToken).ConfigureAwait(false);
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
