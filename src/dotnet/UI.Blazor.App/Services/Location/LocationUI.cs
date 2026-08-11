using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Public API for the local user's location sharing: forwards start/stop to <see cref="LiveLocationReporter"/>
/// (which owns the shares state) and posts one-shot current-location messages.
/// </summary>
public class LocationUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private const string OwnMarkerId = "own-location";
    private static readonly TimeSpan RemainingTextUpdatePeriod = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CurrentLocationRefreshPeriod = TimeSpan.FromSeconds(10);

    private readonly MutableState<bool> _isRefreshing = hub.StateFactory.NewMutable(
        false,
        StateCategories.Get(typeof(LocationUI), nameof(_isRefreshing)));

    private ILocationTracker Tracker => field ??= Hub.Services.GetRequiredService<ILocationTracker>();
    private LiveLocationReporter Reporter => field ??= Hub.Services.GetRequiredService<LiveLocationReporter>();
    private LocationPermissionHandler LocationPermission
        => field ??= Hub.Services.GetRequiredService<LocationPermissionHandler>();
    private IAuthors Authors => Hub.Authors;
    private ISharedLocations SharedLocations => Hub.SharedLocations;

    [ComputeMethod]
    public virtual async Task<bool> IsAnyoneSharing(ChatId chatId, CancellationToken cancellationToken)
    {
        var participants = await ListParticipants(chatId, cancellationToken).ConfigureAwait(false);
        return participants.Count > 0;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<Author>> ListAuthors(ChatId chatId, CancellationToken cancellationToken)
    {
        var participants = await ListParticipants(chatId, cancellationToken).ConfigureAwait(false);
        return participants.Select(x => x.Author).ToList();
    }

    public Task<IReadOnlyList<LocationParticipant>> ListParticipants(ChatId chatId, CancellationToken cancellationToken)
        => ListParticipants(chatId, null, cancellationToken);

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<LocationParticipant>> ListParticipants(
        ChatId chatId,
        SharedLocationId? locationId,
        CancellationToken cancellationToken)
    {
        var locations = await GetLocations().ConfigureAwait(false);
        if (locations.Count == 0)
            return [];

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var participants = await locations.Select(GetParticipant)
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return participants
            .SkipNullItems()
            .OrderByDescending(p => p.IsOwn)
            .ThenBy(p => p.Author.Avatar.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        async Task<IReadOnlyList<SharedLocation>> GetLocations() {
            if (locationId is null)
                return await SharedLocations.ListLive(Session, chatId, cancellationToken).ConfigureAwait(false);

            var location = await SharedLocations.Get(Session, chatId, locationId, cancellationToken)
                .ConfigureAwait(false);
            if (location is null)
                return [];

            if (location.Duration == TimeSpan.Zero)
                return [location];

            var liveLocations = await SharedLocations.ListLive(Session, chatId, cancellationToken)
                .ConfigureAwait(false);
            return liveLocations.Any(x => x.Id == locationId)
                ? liveLocations
                : [location, ..liveLocations];
        }

        async Task<LocationParticipant?> GetParticipant(SharedLocation sharedLocation) {
            var author = await Authors.Get(Session, sharedLocation.ChatId, sharedLocation.AuthorId, cancellationToken)
                .ConfigureAwait(false);
            if (author is null)
                return null;

            var isOwn = ownAuthor != null && author.Id == ownAuthor.Id;
            return new LocationParticipant(sharedLocation, author, isOwn, ToMapMarker(sharedLocation, author));
        }
    }

    [ComputeMethod]
    public virtual async Task<bool> IsOneTime(ChatId chatId, SharedLocationId id, CancellationToken cancellationToken)
    {
        // Duration is immutable, so capture Get in isolation - no dependency on it, no recompute per fix.
        Computed<SharedLocation?> cLocation;
        using (Computed.BeginIsolation())
            cLocation = await Computed
                .Capture(() => SharedLocations.Get(Session, chatId, id, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

        return cLocation.Value is not { Duration.Ticks: > 0 };
    }

    [ComputeMethod]
    public virtual async Task<LocationCountdown?> GetCountdown(
        ChatId chatId,
        SharedLocationId locationId,
        CancellationToken cancellationToken)
    {
        var location = await SharedLocations.Get(Session, chatId, locationId, cancellationToken)
            .ConfigureAwait(false);
        var now = Hub.Clocks.ServerClock.Now;
        if (location?.IsLive(now) != true)
            return null;

        if (location.IsUnlimited)
            return LocationCountdown.Unlimited;

        var remaining = location.LiveUntil - now;
        var delay = TimeSpanExt.Min(RemainingTextUpdatePeriod, remaining) + TimeSpan.FromMilliseconds(250);
        Computed.GetCurrent().Invalidate(delay, false);
        return new LocationCountdown(remaining, location.Duration);
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<bool> IsOwnLive(ChatId chatId, CancellationToken cancellationToken)
        // Consolidated: any sharer's fix invalidates GetOwnLive, and almost none of that flips this bool.
        => await GetOwnLive(chatId, cancellationToken).ConfigureAwait(false) != null;

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
        var locations = await SharedLocations.ListLive(Session, authorId.ChatId, cancellationToken)
            .ConfigureAwait(false);
        return locations.FirstOrDefault(x => x.AuthorId == authorId);
    }

    [ComputeMethod]
    public virtual async Task<MapMarker?> GetOwnMarker(ChatId chatId, CancellationToken cancellationToken)
    {
        if (await GetOwnCurrentOrSharedLocation(chatId, cancellationToken).ConfigureAwait(false) is not { } point)
            return null;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        return ownAuthor is null
            ? new MapMarker(OwnMarkerId, point, IsOwnLocation: true)
            : ToMapMarker(OwnMarkerId, point, ownAuthor, isOwnLocation: true);
    }

    [ComputeMethod]
    public virtual async Task<MapMarker?> GetMarker(
        ChatId chatId,
        SharedLocationId locationId,
        CancellationToken cancellationToken)
    {
        var location = await SharedLocations.Get(Session, chatId, locationId, cancellationToken)
            .ConfigureAwait(false);
        if (location is null)
            return null;

        var author = await Authors.Get(Session, chatId, location.AuthorId, cancellationToken).ConfigureAwait(false);
        return author is null
            ? new MapMarker(location.Id.Value, location.Point)
            : ToMapMarker(location, author);
    }

    [ComputeMethod]
    public virtual async Task<GeoPoint?> GetOwnCurrentOrSharedLocation(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        if (await GetCurrentLocation(cancellationToken).ConfigureAwait(false) is { } current)
            return current;

        var ownLive = await GetOwnLive(chatId, cancellationToken).ConfigureAwait(false);
        return ownLive?.Point;
    }

    [ComputeMethod]
    public virtual async Task<GeoPoint?> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var fix = await GetCurrentFix(false, cancellationToken).ConfigureAwait(false);
        return fix?.Point;
    }

    public Task<bool> IsRefreshing(CancellationToken cancellationToken)
        => _isRefreshing.Use(cancellationToken);

    [ComputeMethod]
    public virtual async Task<bool?> IsPermissionGranted(CancellationToken cancellationToken)
    {
        // null means "undetermined" here: web reports it for "prompt", Android - for a soft denial.
        var isGranted = await LocationPermission.Cached.Use(cancellationToken).ConfigureAwait(false);
        return isGranted ?? await LocationPermission.Check(cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<GeoTrackingError?> GetTrackingError(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (ownAuthor is null)
            return null;

        if (!await IsOwnLive(chatId, cancellationToken).ConfigureAwait(false))
            return null;

        return await Tracker.Error.Use(cancellationToken).ConfigureAwait(false);
    }

    public async Task ShowShareLocationModal(ChatId chatId)
    {
        var isSharing = await IsOwnLive(chatId, Hub.StopToken).ConfigureAwait(true);
        var model = new ShareLocationModal.Model { ChatId = chatId, IsSharing = isSharing };
        var modalRef = await Hub.ModalUI.Show(model, Hub.StopToken).ConfigureAwait(true);
        await modalRef.WhenClosed.ConfigureAwait(true);

        if (model.StopRequested) {
            await StopSharing(chatId, Hub.StopToken).ConfigureAwait(true);
            return;
        }

        if (model.SendCurrentRequested) {
            if (!await LocationPermission.CheckOrRequest().ConfigureAwait(true))
                return;

            await SendCurrentLocation(chatId, Hub.StopToken).ConfigureAwait(true);
            return;
        }

        // A place the user picked on the map isn't their position, so it needs no location permission.
        if (model.PickedPoint is { } pickedPoint) {
            await SendLocation(chatId, pickedPoint, true, Hub.StopToken).ConfigureAwait(true);
            return;
        }

        if (model.SelectedDuration is not { } duration)
            return;

        if (!await LocationPermission.CheckOrRequest(Hub.StopToken).ConfigureAwait(true))
            return;

        StartSharing(chatId, duration);
    }

    public Task ShowLocationMapModal(ChatId chatId, SharedLocationId? locationId = null)
        => Hub.ModalUI.Show(new LocationMapModal.Model(chatId, locationId), Hub.StopToken);

    public void StartSharing(ChatId chatId, TimeSpan duration)
        => Reporter.StartSharing(chatId, duration);

    public Task StopSharing(ChatId chatId, CancellationToken cancellationToken)
        => Reporter.StopSharing(chatId, cancellationToken);

    public async Task SendCurrentLocation(ChatId chatId, CancellationToken cancellationToken)
    {
        if (await Tracker.Get(false, cancellationToken).ConfigureAwait(false) is not { Point: var point })
            return;

        await SendLocation(chatId, point, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendLocation(
        ChatId chatId,
        GeoPoint point,
        bool isPlace,
        CancellationToken cancellationToken)
    {
        var change = Change.Create(new SharedLocationDiff {
            Point = point,
            LiveDuration = TimeSpan.Zero,
            IsPlace = isPlace,
        });
        var shared = await Commander.Call(
                new SharedLocations_Change(Session, chatId, null, change),
                cancellationToken)
            .ConfigureAwait(false);
        if (shared is null)
            return;

        var command = new Chats_UpsertEntry(Session, chatId, null) { LocationId = shared.Id };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    public Task RunLocationRefreshLoop(bool mustTroubleshootOnFirstRun, CancellationToken cancellationToken)
    {
        return AsyncChain.From(Refresh)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 10), Log)
            .AppendDelay(CurrentLocationRefreshPeriod)
            .CycleForever()
            .RunIsolated(cancellationToken);

        async Task Refresh(CancellationToken cancellationToken1) {
            await RefreshCurrentLocation(mustTroubleshootOnFirstRun, cancellationToken1).ConfigureAwait(false);
            mustTroubleshootOnFirstRun = false;
        }
    }

    public Task<GeoPoint?> RefreshCurrentLocation(
        bool mustTroubleshoot,
        CancellationToken cancellationToken = default)
    {
        // The flag is set synchronously, so IsRefreshing is already true when the caller renders next.
        _isRefreshing.Value = true;
        return Refresh();

        async Task<GeoPoint?> Refresh() {
            try {
                var isGranted = await LocationPermission
                    .CheckOrRequest(true, mustTroubleshoot, cancellationToken)
                    .ConfigureAwait(false);
                if (!isGranted)
                    return null;

                // The fresh fix lands in the tracker, so GetCurrentFix and its dependents recompute with it.
                var fix = await Tracker.Get(true, cancellationToken).ConfigureAwait(false);
                return fix?.Point;
            }
            finally {
                _isRefreshing.Value = false;
            }
        }
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<GeoFix?> GetCurrentFix(bool mustBeFresh, CancellationToken cancellationToken)
    {
        if (await IsPermissionGranted(cancellationToken).ConfigureAwait(false) != true)
            return null;

        return await Tracker.Get(mustBeFresh, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private MapMarker ToMapMarker(SharedLocation location, Author author)
        // A place isn't where its author is, so it must not wear their avatar.
        => location.IsPlace
            ? new MapMarker(location.Id.Value, location.Point)
            : ToMapMarker(location.Id.Value, location.Point, author);

    private MapMarker ToMapMarker(string id, GeoPoint point, Author author, bool isOwnLocation = false)
    {
        var pictureUrl = author.Avatar.Picture is { } picture ? UrlMapper.PicturePreview128Url(picture) : "";
        var avatarKey = pictureUrl.IsNullOrEmpty()
            ? (author.Avatar.Picture?.AvatarKey).NullIfEmpty() ?? DefaultUserPicture.GetAvatarKey(author.Id.Value)
            : "";
        return new MapMarker(
            id,
            point,
            author.Avatar.Name,
            pictureUrl.NullIfEmpty(),
            avatarKey.NullIfEmpty(),
            isOwnLocation);
    }
}

public sealed record LocationParticipant(SharedLocation Location, Author Author, bool IsOwn, MapMarker MapMarker);

/// <summary>
/// Remaining/total time of a live location share; <see cref="Unlimited"/> for shares with no expiration.
/// </summary>
public sealed record LocationCountdown(TimeSpan Remaining, TimeSpan Duration)
{
    public static readonly LocationCountdown Unlimited = new(TimeSpan.MaxValue, TimeSpan.MaxValue);
    private static readonly TimeSpan RoundingTolerance = TimeSpan.FromSeconds(2);

    public bool IsUnlimited => Duration == TimeSpan.MaxValue;
    public double Fraction => IsUnlimited ? 1 : Math.Clamp(Remaining / Duration, 0, 1);
    public string Text {
        get {
            if (IsUnlimited)
                return "";

            if (Remaining.TotalHours >= 1)
                return $"{(int)Math.Ceiling(Remaining.TotalHours)}h";

            var minutes = (int)Math.Ceiling((Remaining - RoundingTolerance).TotalMinutes);
            return Math.Max(1, minutes).ToString();
        }
    }
}
