using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Video;
using ActualLab.Interception;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Components.VideoPanel;

public class VideoPanelLayoutCalculator : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private static readonly TimeSpan FocusDebounceDelay = TimeSpan.FromSeconds(1.5);
    private const int MaxFocusHistory = 4;
    private const int MaxDisplaySlotsWide = 4; // focused + up to 3 on sidebar
    private const int MaxDisplaySlotsNarrow = 3; // focused + up to 2 on sidebar
    private readonly TaskCompletionSource _whenInitializedSource = TaskCompletionSourceExt.New();
    private readonly MutableState<VideoPanelLayout> _layout;
    private readonly MutableState<ImmutableArray<AuthorId>> _focusedSpeakerIds;
    private readonly Lock _trackFocusLock = new Lock();
    private CancellationTokenSource? _focusDebounceCts;
    private AuthorId? _pendingFocusCandidate;

    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;

    public ChatId ChatId { get; private set; } = default!;

    public VideoPanelLayoutCalculator(AppUIHub hub) : base(hub)
    {
        _layout = hub.StateFactory.NewMutable(VideoPanelLayout.New);
        _focusedSpeakerIds = StateFactory.NewMutable(ImmutableArray<AuthorId>.Empty);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public void Initialize(ChatId chatId)
    {
        if (!_whenInitializedSource.TrySetResult())
            throw StandardError.Constraint("Already initialized");
        ChatId = chatId;
    }

    public Task<VideoPanelLayout> GetLayout(CancellationToken cancellationToken)
        => _layout.Use(cancellationToken);

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await _whenInitializedSource.Task.ConfigureAwait(false);
        var baseChains = new[] {
            AsyncChain.From(TrackFocusedSpeaker),
            AsyncChain.From(CalculateLayout),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        await (
            from chain in baseChains
            select chain
                .WithTransiencyResolver(TransiencyResolvers.PreferTransient)
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<ActiveSpeakerState> GetActiveSpeakerState(CancellationToken cancellationToken)
    {
        var chatId = ChatId;
        var audioStreamingAuthorIds = await Hub.LiveStreamUI
            .GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var activeVideoStreams = await ChatVideoUI.GetActiveVideoStreams(chatId, cancellationToken)
            .ConfigureAwait(false);
        var ownAuthor = await Hub.Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var ownAuthorId = ownAuthor?.Id ?? default;

        // Own author is filtered out: focus on this client should always be a
        // remote participant (a remote screencaster or a remote speaker), never
        // ourselves — even when we're the one screencasting.
        var activeVideoAuthorIds = activeVideoStreams
            .Where(s => s.AuthorId != ownAuthorId)
            .Select(s => s.AuthorId)
            .ToHashSet();

        var screenCastAuthorIds = activeVideoStreams
            .Where(s => s.SourceKind == VideoSourceKind.ScreenCast && s.AuthorId != ownAuthorId)
            .Select(s => s.AuthorId)
            .Distinct()
            .ToImmutableArray();

        var speakingWithVideo = audioStreamingAuthorIds
            .Where(activeVideoAuthorIds.Contains)
            .Where(a => a != ownAuthorId)
            .ToArray();

        return new ActiveSpeakerState(speakingWithVideo, screenCastAuthorIds);
    }

    [ComputeMethod]
    protected virtual async Task<LayoutInputs> GetLayoutInputs(CancellationToken cancellationToken)
    {
        var focusedIds = await _focusedSpeakerIds.Use(cancellationToken).ConfigureAwait(false);
        var isOwnRecording = await ChatVideoUI.IsOwnCameraRecording(ChatId, cancellationToken).ConfigureAwait(false);
        var remoteStreams = await ChatVideoUI.GetRemoteStreams(ChatId, cancellationToken).ConfigureAwait(false);
        var screenSize = await Hub.BrowserInfo.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        return new LayoutInputs(
            screenSize.IsNarrow(),
            isOwnRecording,
            remoteStreams,
            focusedIds);
    }

    // Private methods

    private async Task TrackFocusedSpeaker(CancellationToken cancellationToken)
    {
        var cState = await Computed
            .Capture(() => GetActiveSpeakerState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        await foreach (var (state, error) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            // Changes yields error snapshots too, with a null value — skip them
            // instead of NRE-ing on the deconstruct below.
            if (error is not null)
                continue;

            var (speakersWithVideo, screenCastAuthorIds) = state;
            lock (_trackFocusLock) {
                // ScreenCasts are primary-only. Backend/client gates allow only
                // one, but stale/raced entries can overlap briefly; in that case
                // the current speaker's screencast wins.
                if (screenCastAuthorIds.Length != 0) {
                    var current = _focusedSpeakerIds.Value.FirstOrDefault();
                    var focusedScreenCast = screenCastAuthorIds.FirstOrDefault(a => a == current);
                    var speakingScreenCast = speakersWithVideo.FirstOrDefault(screenCastAuthorIds.Contains);
                    var next = speakingScreenCast
                        ?? focusedScreenCast
                        ?? screenCastAuthorIds[0];
                    SetFocused(next);
#pragma warning disable CA1849 // Use async overload
                    _focusDebounceCts?.Cancel();
#pragma warning restore CA1849
                    _focusDebounceCts = null;
                    _pendingFocusCandidate = null;
                    continue;
                }

                UpdateFocusedSpeakers(speakersWithVideo);
            }
        }
    }

    private async Task CalculateLayout(CancellationToken cancellationToken)
    {
        var cState = await Computed
            .Capture(() => GetLayoutInputs(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        await foreach (var (inputs, error) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            // Changes yields error snapshots too, with a null value — skip them
            // instead of NRE-ing on the deconstruct below.
            if (error is not null)
                continue;

            var layout = BuildLayout(inputs);
            if (layout != _layout.Value)
                _layout.Value = layout;
        }
    }

    private void SetFocused(AuthorId newFocused)
    {
        lock (_trackFocusLock) {
            var ids = _focusedSpeakerIds.Value;
            if (ids.Length > 0 && ids[0] == newFocused)
                return;

            // Remove if already in history, then prepend
            ids = ids.Remove(newFocused);
            ids = ids.Insert(0, newFocused);

            // Trim to max length
            if (ids.Length > MaxFocusHistory)
                ids = ids.RemoveRange(MaxFocusHistory, ids.Length - MaxFocusHistory);

            _focusedSpeakerIds.Value = ids;
        }
    }

    private void UpdateFocusedSpeakers(AuthorId[] speakersWithVideo)
    {
        // No candidates — keep last focus
        if (speakersWithVideo.Length == 0)
            return;

        var ids = _focusedSpeakerIds.Value;
        var current = ids.Length > 0 ? (AuthorId?)ids[0] : null;
        if (current != null && speakersWithVideo.Any(a => a == current)) {
            // Current focus is still speaking — keep it, cancel any pending switch
            _focusDebounceCts?.Cancel();
            _focusDebounceCts = null;
            _pendingFocusCandidate = null;
            return;
        }

        var candidate = speakersWithVideo[0];

        // No focus yet — take first speaker immediately
        if (ids.Length == 0) {
            SetFocused(candidate);
            return;
        }

        // Already debouncing this candidate
        if (_pendingFocusCandidate == candidate)
            return;

        // New candidate — start debounce
        _pendingFocusCandidate = candidate;
        _focusDebounceCts?.Cancel();
        _focusDebounceCts = new CancellationTokenSource();
        _ = DebouncedSetFocused(candidate, _focusDebounceCts.Token);
    }

    private async Task DebouncedSetFocused(AuthorId newSpeaker, CancellationToken cancellationToken)
    {
        try {
            await Task.Delay(FocusDebounceDelay, cancellationToken).ConfigureAwait(false);
            SetFocused(newSpeaker);
            _pendingFocusCandidate = null;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Groups streams by author into a primary-plus-PiP pair: if an author has both
    /// a screencast and a camera stream active, the screencast is the primary tile
    /// and the camera is its PiP overlay. An author with only one kind has a null PiP.
    /// </summary>
    private static ImmutableArray<AuthorStreamGroup> BuildDisplayList(
        VideoStreamInfo[] remoteStreams,
        ImmutableArray<AuthorId> focusedIds,
        int maxDisplaySlots)
    {
        // Build per-author grouping. An author may have multiple concurrent streams
        // (screencast + camera, or transient overlap during stream restart). The
        // group pairs a primary stream with an optional PiP overlay — screencast
        // wins as primary, camera becomes the PiP. This replaces the earlier
        // single-stream-per-author dictionary that crashed on duplicate keys.
        var groups = remoteStreams
            .GroupBy(s => s.AuthorId)
            .ToDictionary(g => g.Key, g => AuthorStreamGroup.From(g));

        var display = new List<AuthorStreamGroup>();
        var seen = new HashSet<AuthorId>();
        foreach (var id in focusedIds) {
            if (groups.TryGetValue(id, out var group)) {
                display.Add(group);
                seen.Add(id);
            }
            if (display.Count >= maxDisplaySlots)
                break;
        }

        if (display.Count < maxDisplaySlots) {
            var others = groups.Values
                .Where(g => !seen.Contains(g.Primary.AuthorId))
                .OrderByDescending(g => g.Primary.StartedAt);
            foreach (var group in others) {
                if (seen.Add(group.Primary.AuthorId)) {
                    display.Add(group);
                    if (display.Count >= maxDisplaySlots)
                        break;
                }
            }
        }

        return [..display];
    }

    /// <summary>
    /// Primary stream (screencast if available) plus optional PiP overlay (camera
    /// belonging to the same author, when the author is dual-streaming).
    /// </summary>
    public sealed record AuthorStreamGroup(VideoStreamInfo Primary, VideoStreamInfo? Pip)
    {
        public static AuthorStreamGroup From(IEnumerable<VideoStreamInfo> authorStreams)
        {
            VideoStreamInfo? screenCast = null;
            VideoStreamInfo? camera = null;
            foreach (var s in authorStreams) {
                if (s.SourceKind == VideoSourceKind.ScreenCast)
                    screenCast = s;
                else
                    camera = s;
            }
            return screenCast is not null
                ? new AuthorStreamGroup(screenCast, camera)
                : new AuthorStreamGroup(camera!, null);
        }
    }

    private static VideoPanelLayout BuildLayout(LayoutInputs inputs)
    {
        var (isNarrow, hasOwnCamera, remoteStreams, focusedIds) = inputs;
        var hasRemote = remoteStreams.Length > 0;

        // Build ordered display list from focus history + active streams
        var maxSlots = isNarrow ? MaxDisplaySlotsNarrow : MaxDisplaySlotsWide;
        var displayList = BuildDisplayList(remoteStreams, focusedIds, maxSlots);
        // Only remote screencasts can be primary on this client. Own screencast
        // is intentionally not shown on the local user's screen at all — they
        // already see what they're sharing through the OS-level share UI, and
        // surfacing it here would push remote speakers out of the focused slot.
        var primaryScreenCast = SelectPrimaryRemoteScreenCast(displayList, focusedIds);
        var hasScreenCastPrimary = primaryScreenCast is not null;

        var ownCameraClass = !hasOwnCamera ? ""
            : hasScreenCastPrimary || hasRemote ? "item-x item-0"
            : "item-focused";
        var nextSidebarIndex = ownCameraClass.Contains("item-x") ? 1 : 0;

        // Map display list to stream → CSS class; hide PiP-overlay streams from the
        // main tile grid (they render inside their primary's tile).
        var remoteClasses = new List<RemoteStreamPlayerClass>();
        var pipPairs = new List<PipPair>();
        AuthorStreamGroup? focusedCameraGroup = null;
        if (primaryScreenCast is { } remotePrimary) {
            remoteClasses.Add(new RemoteStreamPlayerClass(remotePrimary.Group.Primary.StreamId.Value, "item-focused"));
            if (remotePrimary.Group.Pip is { } pip)
                pipPairs.Add(new PipPair(remotePrimary.Group.Primary.StreamId.Value, pip));
        }
        else {
            var focusedGroup = displayList.FirstOrDefault();
            if (focusedGroup is not null) {
                focusedCameraGroup = focusedGroup;
                remoteClasses.Add(new RemoteStreamPlayerClass(focusedGroup.Primary.StreamId.Value, "item-focused"));
                if (focusedGroup.Pip is { } pip)
                    pipPairs.Add(new PipPair(focusedGroup.Primary.StreamId.Value, pip));
            }
        }

        foreach (var stream in GetSidebarCameras(displayList, primaryScreenCast, focusedCameraGroup)) {
            var cls = $"item-x item-{nextSidebarIndex}";
            nextSidebarIndex++;
            remoteClasses.Add(new RemoteStreamPlayerClass(stream.StreamId.Value, cls));
        }

        return new VideoPanelLayout(ownCameraClass, [..remoteClasses], [..pipPairs]);
    }

    private static RemoteScreenCastPrimary? SelectPrimaryRemoteScreenCast(
        ImmutableArray<AuthorStreamGroup> displayList,
        ImmutableArray<AuthorId> focusedIds)
    {
        var remoteScreenCasts = displayList
            .Where(g => g.Primary.SourceKind == VideoSourceKind.ScreenCast)
            .ToDictionary(g => g.Primary.AuthorId, g => g);
        if (remoteScreenCasts.Count == 0)
            return null;

        foreach (var id in focusedIds) {
            if (remoteScreenCasts.TryGetValue(id, out var group))
                return new RemoteScreenCastPrimary(group);
        }

        return remoteScreenCasts.Values.FirstOrDefault() is { } fallback
            ? new RemoteScreenCastPrimary(fallback)
            : null;
    }

    private static IEnumerable<VideoStreamInfo> GetSidebarCameras(
        ImmutableArray<AuthorStreamGroup> displayList,
        RemoteScreenCastPrimary? primaryScreenCast,
        AuthorStreamGroup? focusedCameraGroup)
    {
        foreach (var group in displayList) {
            if (primaryScreenCast is { } remotePrimary
                && remotePrimary.Group.Primary.StreamId == group.Primary.StreamId)
                continue;
            if (focusedCameraGroup is not null
                && focusedCameraGroup.Primary.StreamId == group.Primary.StreamId)
                continue;

            if (group.Primary.SourceKind == VideoSourceKind.Camera) {
                yield return group.Primary;
                continue;
            }

            if (group.Pip is { } camera)
                yield return camera;
        }
    }

    // Nested types

    protected sealed record ActiveSpeakerState(AuthorId[] SpeakersWithVideo, ImmutableArray<AuthorId> ScreenCastAuthorIds)
    {
        public static readonly ActiveSpeakerState None = new([], []);
    }

    protected sealed record LayoutInputs(
        bool IsNarrowScreen,
        bool HasOwnCameraPreview,
        VideoStreamInfo[] RemoteStreams,
        ImmutableArray<AuthorId> FocusedSpeakerIds)
    {
        public static readonly LayoutInputs None = new(true, false, [], []);
    }

    private sealed record RemoteScreenCastPrimary(AuthorStreamGroup Group);
}

public record RemoteStreamPlayerClass(string StreamId, string Class);

public record PipPair(string PrimaryStreamId, VideoStreamInfo Pip);

public record VideoPanelLayout(
    string OwnCameraPreviewClass,
    ImmutableArray<RemoteStreamPlayerClass> RemoteStreamPlayerClasses,
    ImmutableArray<PipPair> PipPairs)
{
    public static readonly VideoPanelLayout New = new("", [], []);

#pragma warning disable CA1822 // Member can be static
    public string LayoutClass
        => "video-panel-layout__sidebar";
#pragma warning restore CA1822

    // Real video tiles (focused + sidebar cameras, excluding PiP overlays) — drives
    // the equal-split grid template (`tiles-N`) in the "equal" layout, see video-panel.css.
    public int TileCount
        => (string.IsNullOrEmpty(OwnCameraPreviewClass) ? 0 : 1) + RemoteStreamPlayerClasses.Length;

    public string GetRemoteStreamPlayerClass(StreamId streamId)
        => RemoteStreamPlayerClasses.FirstOrDefault(c => c.StreamId == streamId.Value)?.Class ?? "";

    public VideoStreamInfo? GetPipStreamFor(StreamId primaryStreamId)
        => PipPairs.FirstOrDefault(p => p.PrimaryStreamId == primaryStreamId.Value)?.Pip;
}
