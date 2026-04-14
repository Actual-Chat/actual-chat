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
            AsyncChain.From(SyncFocusedSpeaker),
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

    private async Task SyncFocusedSpeaker(CancellationToken cancellationToken)
    {
        var cState = await Computed
            .Capture(() => GetActiveSpeakerState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        await foreach (var (state, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            var (speakingWithVideo, remoteAuthorIds, screencastAuthorId) = state;

            // Screencast always takes focus (no debounce)
            if (screencastAuthorId is { } scAuthor) {
                SetFocused(scAuthor);
                _focusDebounceCts?.CancelAsync();
                _focusDebounceCts = null;
                _pendingFocusCandidate = null;
                continue;
            }

            UpdateActiveSpeakers(speakingWithVideo);

            // Ensure we have at least one focused candidate if remotes exist.
            // BuildDisplayList handles filtering out disconnected authors.
            var ids = _focusedSpeakerIds.Value;
            if (ids.Length == 0 && remoteAuthorIds.Length > 0)
                _focusedSpeakerIds.Value = ids.Add(remoteAuthorIds[0]);
        }
    }

    [ComputeMethod]
    protected virtual async Task<ActiveSpeakerState> GetActiveSpeakerState(CancellationToken cancellationToken)
    {
        var chatId = ChatId;
        var audioStreamingAuthorIds = await Hub.LiveStreamUI
            .GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var videoStreams = await ChatVideoUI.GetActiveVideoStreams(chatId, cancellationToken)
            .ConfigureAwait(false);

        // Filter out own author
        var ownAuthor = await Hub.Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var remoteVideoAuthorIds = videoStreams
            .Select(s => s.AuthorId)
            .Where(a => ownAuthor?.Id != a)
            .ToHashSet();

        // Check for screencast among remote streams (not own)
        var screencastAuthorId = videoStreams
            .Where(s => s.StreamKind == StreamKind.Screencast && ownAuthor?.Id != s.AuthorId)
            .Select(s => (AuthorId?)s.AuthorId)
            .FirstOrDefault();

        var speakingWithVideo = audioStreamingAuthorIds
            .Where(remoteVideoAuthorIds.Contains)
            .ToArray();

        return new ActiveSpeakerState(speakingWithVideo, remoteVideoAuthorIds.ToArray(), screencastAuthorId);
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

    private void UpdateActiveSpeakers(AuthorId[] speakingWithVideo)
    {
        var ids = _focusedSpeakerIds.Value;
        var current = ids.Length > 0 ? (AuthorId?)ids[0] : null;
        if (current != null && speakingWithVideo.Any(a => a == current)) {
            // Current focus is still speaking — keep it, cancel any pending switch
            _focusDebounceCts?.Cancel();
            _focusDebounceCts = null;
            _pendingFocusCandidate = null;
            return;
        }

        // No candidates — keep last focus
        if (speakingWithVideo.Length == 0)
            return;

        var candidate = speakingWithVideo[0];

        // Already debouncing this candidate
        if (_pendingFocusCandidate == candidate)
            return;

        // New candidate — start debounce
        _pendingFocusCandidate = candidate;
        _focusDebounceCts?.Cancel();
        _focusDebounceCts = new CancellationTokenSource();
        _ = DebouncedFocusSwitch(candidate, _focusDebounceCts.Token);
    }

    private async Task DebouncedFocusSwitch(AuthorId newSpeaker, CancellationToken cancellationToken)
    {
        try {
            await Task.Delay(FocusDebounceDelay, cancellationToken).ConfigureAwait(false);
            SetFocused(newSpeaker);
            _pendingFocusCandidate = null;
        }
        catch (OperationCanceledException) { }
    }

    private static ImmutableArray<AuthorId> BuildDisplayList(
        ImmutableArray<AuthorId> focusedIds,
        VideoStreamInfo[] remoteStreams,
        int maxDisplaySlots)
    {
        // Build set of active remote author IDs for fast lookup
        var activeRemoteAuthors = remoteStreams.Select(s => s.AuthorId).ToHashSet();

        // Start with focused authors that still have active streams
        var display = new List<AuthorId>();
        foreach (var id in focusedIds) {
            if (activeRemoteAuthors.Contains(id))
                display.Add(id);
            if (display.Count >= maxDisplaySlots)
                break;
        }

        // Fill remaining slots with other participants, ordered by StartedAt descending
        if (display.Count < maxDisplaySlots) {
            var displaySet = display.ToHashSet();
            var others = remoteStreams
                .Where(s => !displaySet.Contains(s.AuthorId))
                .OrderByDescending(s => s.StartedAt)
                .Select(s => s.AuthorId)
                .Distinct();
            foreach (var id in others) {
                display.Add(id);
                if (display.Count >= maxDisplaySlots)
                    break;
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
        var displayList = BuildDisplayList(focusedIds, remoteStreams, maxSlots);

        // Own stream class
        var ownClass = !hasOwn ? ""
            : !hasRemote ? "item-focused"
            : "item-x item-0";

        // Map display list to stream → CSS class
        // displayList[0] → "item-focused"
        // displayList[1+] → "item-x item-{i}" (sidebar, offset by 1 when own takes item-0)
        var sidebarOffset = hasOwn ? 1 : 0;
        var remoteClasses = new List<RemoteStreamPlayerClass>();
        for (var i = 0; i < displayList.Length; i++) {
            var authorId = displayList[i];
            var stream = remoteStreams.FirstOrDefault(s => s.AuthorId == authorId);
            if (stream is null)
                continue;

            var cls = i == 0
                ? "item-focused"
                : $"item-x item-{i + sidebarOffset}";
            remoteClasses.Add(new RemoteStreamPlayerClass(stream.StreamId.Value, cls));
        }

        return new VideoPanelLayout(ownClass, [..remoteClasses]);
    }

    // Nested types

    protected sealed record ActiveSpeakerState(AuthorId[] SpeakingWithVideo, AuthorId[] RemoteVideoAuthorIds, AuthorId? ScreencastAuthorId)
    {
        public static readonly ActiveSpeakerState None = new([], [], null);
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
