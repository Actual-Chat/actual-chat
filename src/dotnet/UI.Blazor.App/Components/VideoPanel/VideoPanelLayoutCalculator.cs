using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
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

        var activeVideoAuthorIds = activeVideoStreams
            .Select(s => s.AuthorId)
            .ToHashSet();

        var screencastAuthorIds = activeVideoStreams
            .Where(s => s.StreamKind == StreamKind.Screencast)
            .Select(s => s.AuthorId)
            .Distinct()
            .ToImmutableArray();

        var speakingWithVideo = audioStreamingAuthorIds
            .Where(activeVideoAuthorIds.Contains)
            .ToArray();

        return new ActiveSpeakerState(speakingWithVideo, screencastAuthorIds);
    }

    [ComputeMethod]
    protected virtual async Task<LayoutInputs> GetLayoutInputs(CancellationToken cancellationToken)
    {
        var focusedIds = await _focusedSpeakerIds.Use(cancellationToken).ConfigureAwait(false);
        var isOwnRecording = await ChatVideoUI.IsOwnWebcamRecording(ChatId, cancellationToken).ConfigureAwait(false);
        var isOwnScreencasting = await ChatVideoUI.IsOwnScreencasting(ChatId, cancellationToken).ConfigureAwait(false);
        var ownAuthor = isOwnRecording || isOwnScreencasting
            ? await Hub.Authors.GetOwn(Session, ChatId, cancellationToken).ConfigureAwait(false)
            : null;
        var remoteStreams = await ChatVideoUI.GetRemoteStreams(ChatId, cancellationToken).ConfigureAwait(false);
        var screenSize = await Hub.BrowserInfo.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        return new LayoutInputs(
            screenSize.IsNarrow(),
            isOwnRecording,
            isOwnScreencasting,
            ownAuthor?.Id,
            remoteStreams,
            focusedIds);
    }

    // Private methods

    private async Task TrackFocusedSpeaker(CancellationToken cancellationToken)
    {
        var cState = await Computed
            .Capture(() => GetActiveSpeakerState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        await foreach (var (state, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            var (speakersWithVideo, screencastAuthorIds) = state;
            lock (_trackFocusLock) {
                // Screencasts are primary-only. Backend/client gates allow only
                // one, but stale/raced entries can overlap briefly; in that case
                // the current speaker's screencast wins.
                if (screencastAuthorIds.Length != 0) {
                    var current = _focusedSpeakerIds.Value.FirstOrDefault();
                    var focusedScreencast = screencastAuthorIds.FirstOrDefault(a => a == current);
                    var speakingScreencast = speakersWithVideo.FirstOrDefault(screencastAuthorIds.Contains);
                    var next = speakingScreencast
                        ?? focusedScreencast
                        ?? screencastAuthorIds[0];
                    SetFocused(next);
                    _focusDebounceCts?.Cancel();
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

        await foreach (var (inputs, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
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
    /// a screencast and a webcam stream active, the screencast is the primary tile
    /// and the webcam is its PiP overlay. An author with only one kind has a null PiP.
    /// </summary>
    private static ImmutableArray<AuthorStreamGroup> BuildDisplayList(
        VideoStreamInfo[] remoteStreams,
        ImmutableArray<AuthorId> focusedIds,
        int maxDisplaySlots)
    {
        // Build per-author grouping. An author may have multiple concurrent streams
        // (screencast + webcam, or transient overlap during stream restart). The
        // group pairs a primary stream with an optional PiP overlay — screencast
        // wins as primary, webcam becomes the PiP. This replaces the earlier
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
    /// Primary stream (screencast if available) plus optional PiP overlay (webcam
    /// belonging to the same author, when the author is dual-streaming).
    /// </summary>
    public sealed record AuthorStreamGroup(VideoStreamInfo Primary, VideoStreamInfo? Pip)
    {
        public static AuthorStreamGroup From(IEnumerable<VideoStreamInfo> authorStreams)
        {
            VideoStreamInfo? screencast = null;
            VideoStreamInfo? webcam = null;
            foreach (var s in authorStreams) {
                if (s.StreamKind == StreamKind.Screencast)
                    screencast = s;
                else
                    webcam = s;
            }
            return screencast is not null
                ? new AuthorStreamGroup(screencast, webcam)
                : new AuthorStreamGroup(webcam!, null);
        }
    }

    private static VideoPanelLayout BuildLayout(LayoutInputs inputs)
    {
        var (isNarrow, hasOwnWebcam, hasOwnScreencast, ownAuthorId, remoteStreams, focusedIds) = inputs;
        var hasRemote = remoteStreams.Length > 0;

        // Build ordered display list from focus history + active streams
        var maxSlots = isNarrow ? MaxDisplaySlotsNarrow : MaxDisplaySlotsWide;
        var displayList = BuildDisplayList(remoteStreams, focusedIds, maxSlots);
        var primaryScreencast = SelectPrimaryScreencast(displayList, hasOwnScreencast, ownAuthorId, focusedIds);
        var hasScreencastPrimary = primaryScreencast is not null || hasOwnScreencast;
        var ownScreencastIsPrimary = primaryScreencast is OwnScreencastPrimary;

        var ownScreencastClass = ownScreencastIsPrimary ? "item-focused" : "";
        var ownWebcamClass = !hasOwnWebcam ? ""
            : hasScreencastPrimary || hasRemote ? "item-x item-0"
            : "item-focused";
        var nextSidebarIndex = ownWebcamClass.Contains("item-x") ? 1 : 0;

        // Map display list to stream → CSS class; hide PiP-overlay streams from the
        // main tile grid (they render inside their primary's tile).
        var remoteClasses = new List<RemoteStreamPlayerClass>();
        var pipPairs = new List<PipPair>();
        AuthorStreamGroup? focusedWebcamGroup = null;
        if (primaryScreencast is RemoteScreencastPrimary remotePrimary) {
            remoteClasses.Add(new RemoteStreamPlayerClass(remotePrimary.Group.Primary.StreamId.Value, "item-focused"));
            if (remotePrimary.Group.Pip is { } pip)
                pipPairs.Add(new PipPair(remotePrimary.Group.Primary.StreamId.Value, pip));
        }
        else if (!hasScreencastPrimary) {
            var focusedGroup = displayList.FirstOrDefault();
            if (focusedGroup is not null) {
                focusedWebcamGroup = focusedGroup;
                remoteClasses.Add(new RemoteStreamPlayerClass(focusedGroup.Primary.StreamId.Value, "item-focused"));
                if (focusedGroup.Pip is { } pip)
                    pipPairs.Add(new PipPair(focusedGroup.Primary.StreamId.Value, pip));
            }
        }

        foreach (var stream in GetSidebarWebcams(displayList, primaryScreencast, focusedWebcamGroup)) {
            var cls = $"item-x item-{nextSidebarIndex}";
            nextSidebarIndex++;
            remoteClasses.Add(new RemoteStreamPlayerClass(stream.StreamId.Value, cls));
        }

        return new VideoPanelLayout(ownWebcamClass, ownScreencastClass, [..remoteClasses], [..pipPairs]);
    }

    private static ScreencastPrimary? SelectPrimaryScreencast(
        ImmutableArray<AuthorStreamGroup> displayList,
        bool hasOwnScreencast,
        AuthorId? ownAuthorId,
        ImmutableArray<AuthorId> focusedIds)
    {
        var remoteScreencasts = displayList
            .Where(g => g.Primary.StreamKind == StreamKind.Screencast)
            .ToDictionary(g => g.Primary.AuthorId, g => g);
        if (!hasOwnScreencast && remoteScreencasts.Count == 0)
            return null;

        foreach (var id in focusedIds) {
            if (hasOwnScreencast && ownAuthorId == id)
                return OwnScreencastPrimary.Instance;
            if (remoteScreencasts.TryGetValue(id, out var group))
                return new RemoteScreencastPrimary(group);
        }

        if (hasOwnScreencast)
            return OwnScreencastPrimary.Instance;
        return remoteScreencasts.Values.FirstOrDefault() is { } fallback
            ? new RemoteScreencastPrimary(fallback)
            : null;
    }

    private static IEnumerable<VideoStreamInfo> GetSidebarWebcams(
        ImmutableArray<AuthorStreamGroup> displayList,
        ScreencastPrimary? primaryScreencast,
        AuthorStreamGroup? focusedWebcamGroup)
    {
        foreach (var group in displayList) {
            if (primaryScreencast is RemoteScreencastPrimary remotePrimary
                && remotePrimary.Group.Primary.StreamId == group.Primary.StreamId)
                continue;
            if (focusedWebcamGroup is not null
                && focusedWebcamGroup.Primary.StreamId == group.Primary.StreamId)
                continue;

            if (group.Primary.StreamKind == StreamKind.Webcam) {
                yield return group.Primary;
                continue;
            }

            if (group.Pip is { } webcam)
                yield return webcam;
        }
    }

    // Nested types

    protected sealed record ActiveSpeakerState(AuthorId[] SpeakersWithVideo, ImmutableArray<AuthorId> ScreencastAuthorIds)
    {
        public static readonly ActiveSpeakerState None = new([], []);
    }

    protected sealed record LayoutInputs(
        bool IsNarrowScreen,
        bool HasOwnWebcamPreview,
        bool HasOwnScreencastPreview,
        AuthorId? OwnAuthorId,
        VideoStreamInfo[] RemoteStreams,
        ImmutableArray<AuthorId> FocusedSpeakerIds)
    {
        public static readonly LayoutInputs None = new(true, false, false, null, [], []);
    }

    private abstract record ScreencastPrimary;

    private sealed record OwnScreencastPrimary : ScreencastPrimary
    {
        public static readonly OwnScreencastPrimary Instance = new();
    }

    private sealed record RemoteScreencastPrimary(AuthorStreamGroup Group) : ScreencastPrimary;
}

public record RemoteStreamPlayerClass(string StreamId, string Class);

public record PipPair(string PrimaryStreamId, VideoStreamInfo Pip);

public record VideoPanelLayout(
    string OwnWebcamPreviewClass,
    string OwnScreencastPreviewClass,
    ImmutableArray<RemoteStreamPlayerClass> RemoteStreamPlayerClasses,
    ImmutableArray<PipPair> PipPairs)
{
    public static readonly VideoPanelLayout New = new("", "", [], []);

    public string LayoutClass
        => "video-panel-layout__sidebar";

    public string GetRemoteStreamPlayerClass(StreamId streamId)
        => RemoteStreamPlayerClasses.FirstOrDefault(c => c.StreamId == streamId.Value)?.Class ?? "";

    public VideoStreamInfo? GetPipStreamFor(StreamId primaryStreamId)
        => PipPairs.FirstOrDefault(p => p.PrimaryStreamId == primaryStreamId.Value)?.Pip;
}
