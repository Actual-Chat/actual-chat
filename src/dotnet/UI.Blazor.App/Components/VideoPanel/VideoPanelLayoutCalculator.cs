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
        var ownKind = await ChatVideoUI.GetOwnStreamKind(ChatId, cancellationToken).ConfigureAwait(false);
        var remoteStreams = await ChatVideoUI.GetRemoteStreams(ChatId, cancellationToken).ConfigureAwait(false);
        var screenSize = await Hub.BrowserInfo.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        return new LayoutInputs(screenSize.IsNarrow(), ownKind, remoteStreams, focusedIds);
    }

    // Private methods

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

    private static ImmutableArray<VideoStreamInfo> BuildDisplayList(
        VideoStreamInfo[] remoteStreams,
        ImmutableArray<AuthorId> focusedIds,
        int maxDisplaySlots)
    {
        // Build lookup from AuthorId → stream for fast access
        var streamByAuthor = remoteStreams.ToDictionary(s => s.AuthorId);

        // Start with focused authors that still have active streams
        var display = new List<VideoStreamInfo>();
        var seen = new HashSet<AuthorId>();
        foreach (var id in focusedIds) {
            if (streamByAuthor.TryGetValue(id, out var stream)) {
                display.Add(stream);
                seen.Add(id);
            }
            if (display.Count >= maxDisplaySlots)
                break;
        }

        // Fill remaining slots with other participants, ordered by StartedAt descending
        if (display.Count < maxDisplaySlots) {
            var others = remoteStreams
                .Where(s => !seen.Contains(s.AuthorId))
                .OrderByDescending(s => s.StartedAt);
            foreach (var stream in others) {
                if (seen.Add(stream.AuthorId)) {
                    display.Add(stream);
                    if (display.Count >= maxDisplaySlots)
                        break;
                }
            }
        }

        return [..display];
    }

    private static VideoPanelLayout BuildLayout(LayoutInputs inputs)
    {
        var (isNarrow, ownKind, remoteStreams, focusedIds) = inputs;
        var hasRemote = remoteStreams.Length > 0;
        var hasOwn = ownKind is not null;

        // Build ordered display list from focus history + active streams
        var maxSlots = isNarrow ? MaxDisplaySlotsNarrow : MaxDisplaySlotsWide;
        var displayList = BuildDisplayList(remoteStreams, focusedIds, maxSlots);

        // Own stream class
        var ownClass = !hasOwn ? ""
            : !hasRemote ? "item-focused"
            : "item-x item-0";

        // Map display list to stream → CSS class
        var remoteClasses = new List<RemoteStreamPlayerClass>();
        var focusedStream = displayList.FirstOrDefault();
        // displayList[0] → "item-focused"
        if (focusedStream is not null)
            remoteClasses.Add(new RemoteStreamPlayerClass(focusedStream.StreamId.Value, "item-focused"));
        // displayList[1+] → "item-x item-{i}" (sidebar, offset by 1 when own takes item-0)
        var i = hasOwn ? 1 : 0;
        foreach (var stream in displayList.Skip(1)) {
            var cls = $"item-x item-{i}";
            i++;
            remoteClasses.Add(new RemoteStreamPlayerClass(stream.StreamId.Value, cls));
        }

        return new VideoPanelLayout(ownClass, [..remoteClasses]);
    }

    // Nested types

    protected sealed record ActiveSpeakerState(AuthorId[] SpeakersWithVideo, AuthorId? ScreencastAuthorId)
    {
        public static readonly ActiveSpeakerState None = new([], null);
    }

    protected sealed record LayoutInputs(
        bool IsNarrowScreen,
        StreamKind? OwnStreamKind,
        VideoStreamInfo[] RemoteStreams,
        ImmutableArray<AuthorId> FocusedSpeakerIds)
    {
        public static readonly LayoutInputs None = new(true, null, [], []);
    }
}

public record RemoteStreamPlayerClass(string StreamId, string Class);

public record VideoPanelLayout(string OwnStreamingPreviewClass, ImmutableArray<RemoteStreamPlayerClass> RemoteStreamPlayerClasses)
{
    public static readonly VideoPanelLayout New = new("", []);

    public string LayoutClass
        => "video-panel-layout__sidebar";

    public string GetRemoteStreamPlayerClass(StreamId streamId)
        => RemoteStreamPlayerClasses.FirstOrDefault(c => c.StreamId == streamId.Value)?.Class ?? "";
}
