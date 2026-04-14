using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Components.VideoPanel;

public class VideoPanelLayoutCalculator : UIWorkerBase<AppUIHub>
{
    private static readonly TimeSpan FocusDebounceDelay = TimeSpan.FromSeconds(1.5);
    private readonly MutableState<VideoPanelLayout> _layout;
    private readonly MutableState<AuthorId?> _focusedSpeakerId;
    private readonly MutableState<AuthorId?> _previousFocusedSpeakerId;
    private CancellationTokenSource? _focusDebounceCts;
    private AuthorId? _pendingFocusCandidate;

    public ChatId ChatId { get; }

    public VideoPanelLayoutCalculator(AppUIHub hub, ChatId chatId) : base(hub)
    {
        ChatId = chatId;
        _layout = hub.StateFactory.NewMutable(VideoPanelLayout.New);
        _focusedSpeakerId = StateFactory.NewMutable((AuthorId?)null);
        _previousFocusedSpeakerId = StateFactory.NewMutable((AuthorId?)null);
    }

    public Task<VideoPanelLayout> GetLayout(CancellationToken cancellationToken)
        => _layout.Use(cancellationToken);

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
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

    private Task CalculateLayout(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private async Task SyncFocusedSpeaker(CancellationToken cancellationToken)
    {
        var cState = await Computed
            .Capture(() => GetActiveSpeakerState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        await foreach (var (state, _) in cState.Changes(cancellationToken).ConfigureAwait(false)) {
            var (speakingWithVideo, remoteAuthorIds, screencastAuthorId) = state;

            // Screencast always takes focus (no debounce)
            if (screencastAuthorId is not null) {
                var oldFocus = _focusedSpeakerId.Value;
                if (oldFocus != screencastAuthorId) {
                    if (oldFocus != null)
                        _previousFocusedSpeakerId.Value = oldFocus;
                    _focusedSpeakerId.Value = screencastAuthorId;
                }
                _focusDebounceCts?.Cancel();
                _focusDebounceCts = null;
                _pendingFocusCandidate = null;
                continue;
            }

            UpdateActiveSpeakers(speakingWithVideo);

            // Validate focused author is still among remote streams; fallback to first
            var currentFocus = _focusedSpeakerId.Value;
            if (currentFocus != null && remoteAuthorIds.Length > 0 && !remoteAuthorIds.Contains(currentFocus))
                _focusedSpeakerId.Value = null;
            if (_focusedSpeakerId.Value is null && remoteAuthorIds.Length > 0)
                _focusedSpeakerId.Value = remoteAuthorIds[0];
        }
    }

    [ComputeMethod]
    protected virtual async Task<ActiveSpeakerState> GetActiveSpeakerState(CancellationToken cancellationToken)
    {
        var chatId = ChatId;
        var audioStreamingAuthorIds = await Hub.LiveStreamUI
            .GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        var videoStreams = await Hub.ChatVideoUI.GetActiveVideoStreams(chatId, cancellationToken)
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
            .Where(a => remoteVideoAuthorIds.Contains(a))
            .ToArray();

        return new ActiveSpeakerState(speakingWithVideo, remoteVideoAuthorIds.ToArray(), screencastAuthorId);
    }

    // Private methods

    private void UpdateActiveSpeakers(AuthorId[] speakingWithVideo)
    {
        var current = _focusedSpeakerId.Value;
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
            var oldFocus = _focusedSpeakerId.Value;
            if (oldFocus != null && oldFocus != newSpeaker)
                _previousFocusedSpeakerId.Value = oldFocus;
            _focusedSpeakerId.Value = newSpeaker;
            _pendingFocusCandidate = null;
        }
        catch (OperationCanceledException) { }
    }

    // Nested types
    protected sealed record ActiveSpeakerState(AuthorId[] SpeakingWithVideo, AuthorId[] RemoteVideoAuthorIds, AuthorId? ScreencastAuthorId = null)
    {
        public static readonly ActiveSpeakerState None = new([], [], null);
    }
}

public record RemoteStreamPlayerClass(string StreamId, string Class);

public record VideoPanelLayout(string OwnStreamingPreviewClass, RemoteStreamPlayerClass[] RemoteStreamPlayerClasses)
{
    public static readonly VideoPanelLayout New = new("", []);

    public string LayoutClass
        => "video-panel-layout__sidebar";

    public string GetRemoteStreamPlayerClass(StreamId streamId)
        => RemoteStreamPlayerClasses.FirstOrDefault(c => c.StreamId == streamId.Value)?.Class ?? "";
}
