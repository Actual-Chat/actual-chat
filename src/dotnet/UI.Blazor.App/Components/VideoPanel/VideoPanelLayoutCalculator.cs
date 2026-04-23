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
        var removeVideoStreams = await ChatVideoUI.GetActiveVideoStreams(chatId, cancellationToken)
            .ConfigureAwait(false);

        var remoteVideoAuthorIds = removeVideoStreams
            .Select(s => s.AuthorId)
            .ToHashSet();

        // Check for screencast among remote streams (not own)
        var screencastAuthorId = removeVideoStreams
            .Where(s => s.StreamKind == StreamKind.Screencast)
            .Select(s => (AuthorId?)s.AuthorId)
            .FirstOrDefault();

        var speakingWithVideo = audioStreamingAuthorIds
            .Where(remoteVideoAuthorIds.Contains)
            .ToArray();

        return new ActiveSpeakerState(speakingWithVideo, screencastAuthorId);
    }

    [ComputeMethod]
    protected virtual async Task<LayoutInputs> GetLayoutInputs(CancellationToken cancellationToken)
    {
        var focusedIds = await _focusedSpeakerIds.Use(cancellationToken).ConfigureAwait(false);
        // Own-preview slot reflects any local source — webcam when active, else
        // screencast. VideoStreamingPreview picks the actual source per streamKind.
        var isOwnRecording = await ChatVideoUI.IsOwnRecording(ChatId, cancellationToken).ConfigureAwait(false);
        var isOwnScreencasting = await ChatVideoUI.IsOwnScreencasting(ChatId, cancellationToken).ConfigureAwait(false);
        var hasOwnPreview = isOwnRecording || isOwnScreencasting;
        var remoteStreams = await ChatVideoUI.GetRemoteStreams(ChatId, cancellationToken).ConfigureAwait(false);
        var screenSize = await Hub.BrowserInfo.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        return new LayoutInputs(screenSize.IsNarrow(), hasOwnPreview, remoteStreams, focusedIds);
    }

    // Private methods

    private async Task TrackFocusedSpeaker(CancellationToken cancellationToken)
    {
        var cState = await Computed
            .Capture(() => GetActiveSpeakerState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        await foreach (var (state, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            var (speakersWithVideo, screencastAuthorId) = state;
            lock (_trackFocusLock) {
                // Screencast always takes focus (no debounce)
                if (screencastAuthorId is { } scAuthor) {
                    SetFocused(scAuthor);
                    _focusDebounceCts?.CancelAsync();
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
        var (isNarrow, hasOwn, remoteStreams, focusedIds) = inputs;
        var hasRemote = remoteStreams.Length > 0;

        // Build ordered display list from focus history + active streams
        var maxSlots = isNarrow ? MaxDisplaySlotsNarrow : MaxDisplaySlotsWide;
        var displayList = BuildDisplayList(remoteStreams, focusedIds, maxSlots);

        // Own stream class
        var ownClass = !hasOwn ? ""
            : !hasRemote ? "item-focused"
            : "item-x item-0";

        // Map display list to stream → CSS class; hide PiP-overlay streams from the
        // main tile grid (they render inside their primary's tile).
        var remoteClasses = new List<RemoteStreamPlayerClass>();
        var pipPairs = new List<PipPair>();
        var focusedGroup = displayList.FirstOrDefault();
        if (focusedGroup is not null) {
            remoteClasses.Add(new RemoteStreamPlayerClass(focusedGroup.Primary.StreamId.Value, "item-focused"));
            if (focusedGroup.Pip is { } pip)
                pipPairs.Add(new PipPair(focusedGroup.Primary.StreamId.Value, pip));
        }
        var i = hasOwn ? 1 : 0;
        foreach (var group in displayList.Skip(1)) {
            var cls = $"item-x item-{i}";
            i++;
            remoteClasses.Add(new RemoteStreamPlayerClass(group.Primary.StreamId.Value, cls));
            if (group.Pip is { } pip)
                pipPairs.Add(new PipPair(group.Primary.StreamId.Value, pip));
        }

        return new VideoPanelLayout(ownClass, [..remoteClasses], [..pipPairs]);
    }

    // Nested types

    protected sealed record ActiveSpeakerState(AuthorId[] SpeakersWithVideo, AuthorId? ScreencastAuthorId)
    {
        public static readonly ActiveSpeakerState None = new([], null);
    }

    protected sealed record LayoutInputs(
        bool IsNarrowScreen,
        bool HasOwnWebcamPreview,
        VideoStreamInfo[] RemoteStreams,
        ImmutableArray<AuthorId> FocusedSpeakerIds)
    {
        public static readonly LayoutInputs None = new(true, false, [], []);
    }
}

public record RemoteStreamPlayerClass(string StreamId, string Class);

public record PipPair(string PrimaryStreamId, VideoStreamInfo Pip);

public record VideoPanelLayout(
    string OwnStreamingPreviewClass,
    ImmutableArray<RemoteStreamPlayerClass> RemoteStreamPlayerClasses,
    ImmutableArray<PipPair> PipPairs)
{
    public static readonly VideoPanelLayout New = new("", [], []);

    public string LayoutClass
        => "video-panel-layout__sidebar";

    public string GetRemoteStreamPlayerClass(StreamId streamId)
        => RemoteStreamPlayerClasses.FirstOrDefault(c => c.StreamId == streamId.Value)?.Class ?? "";

    public VideoStreamInfo? GetPipStreamFor(StreamId primaryStreamId)
        => PipPairs.FirstOrDefault(p => p.PrimaryStreamId == primaryStreamId.Value)?.Pip;
}
