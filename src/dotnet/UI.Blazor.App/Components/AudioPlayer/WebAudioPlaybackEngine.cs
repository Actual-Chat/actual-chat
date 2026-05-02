using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class WebAudioPlaybackEngine(
    string id,
    TrackInfo info,
    IMediaSource source,
    IAudioPlayerBackend playerBackend,
    IServiceProvider services)
    : IAudioPlaybackEngine
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.AudioPlayer.create";

    private readonly DotNetObjectReference<IAudioPlayerBackend> _blazorRef = DotNetObjectReference.Create(playerBackend);

    private IJSObjectReference? _jsRef;
    private Task? _whenPlayerCreated;
    private CancellationTokenSource? _watchBufferEscalationStateCts;

    private Dispatcher Dispatcher => field ??= services.GetRequiredService<Dispatcher>();
    private ChatAudioUI ChatAudioUI => field ??= services.GetRequiredService<ChatAudioUI>();

    private ILogger Log => field ??= services.LogFor<WebAudioPlaybackEngine>();

    public async Task Play(CancellationToken cancellationToken)
    {
        if (_jsRef != null)
            return;

        var trackInfo = (ChatAudioTrackInfo)info;
        var chat = trackInfo.Chat;
        var author = trackInfo.Author;
        var audioSource = (AudioSource)source;
        var preSkip = audioSource.Format.PreSkip;
        var authorId = author?.Id.Value;
        var sourceRecordedAt = trackInfo.SourceRecordedAt != default
            ? trackInfo.SourceRecordedAt
            : trackInfo.RecordedAt;
        var recordedAtMs = sourceRecordedAt.EpochOffset.TotalMilliseconds;

        var chatId = chat?.Id;
        var bufferEscalation = 0;
        if (chatId != null)
            bufferEscalation = await ChatAudioUI.GetPlaybackBufferEscalation(chatId, cancellationToken).ConfigureAwait(false);

        Log.LogDebug(
            "[WebAudioPlaybackEngine #{AudioTrackPlayerId}] Play: authorId={AuthorId}, recordedAtMs={RecordedAtMs}, bufferEscalation={BufferEscalation}",
            id, authorId, recordedAtMs, bufferEscalation);

        var js = services.JSRuntime();
        var title = author?.Avatar.Name ?? "";
        var album = chat?.Title ?? "";
        var whenPlayerCreated = js.InvokeAsync<IJSObjectReference>(JSCreateMethod,
            CancellationToken.None,
            _blazorRef, id, preSkip, title, album, authorId, recordedAtMs, bufferEscalation);
        _whenPlayerCreated = whenPlayerCreated.AsTask();
        _jsRef = await whenPlayerCreated.ConfigureAwait(false);

        if (chatId != null) {
            _watchBufferEscalationStateCts = cancellationToken.CreateLinkedTokenSource();
            _ = WatchBufferEscalationState(chatId, bufferEscalation, _watchBufferEscalationStateCts.Token);
        }
    }

    public async Task Pause(CancellationToken cancellationToken)
    {
        if (_jsRef == null && _whenPlayerCreated != null)
            await _whenPlayerCreated.ConfigureAwait(false);
        if (_jsRef == null)
            throw StandardError.AudioPlayer.PlayingStateExpected(GetType());

        _ = _jsRef.InvokeVoidAsync("pause", CancellationToken.None);
    }

    public async Task Resume(CancellationToken cancellationToken)
    {
        if (_jsRef == null && _whenPlayerCreated != null)
            await _whenPlayerCreated.ConfigureAwait(false);
        if (_jsRef == null)
            throw StandardError.AudioPlayer.PlayingStateExpected(GetType());

        _ = _jsRef.InvokeVoidAsync("resume", CancellationToken.None);
    }

    public async Task End(bool mustAbort, CancellationToken cancellationToken)
    {
        if (_jsRef == null && _whenPlayerCreated != null)
            await _whenPlayerCreated.ConfigureAwait(false);
        if (_jsRef == null)
            throw StandardError.AudioPlayer.PlayingStateExpected(GetType());

        _ = _jsRef.InvokeVoidAsync("end", CancellationToken.None, mustAbort);
    }

    public ValueTask PushFrame(AudioFrame frame, CancellationToken cancellationToken)
    {
        if (_jsRef == null)
            throw StandardError.StateTransition(GetType(), "Can't process media frame before initialization.");

        var chunk = frame.Data.ToArray(); // JS interop requires byte[] (System.Text.Json)
        _ = _jsRef.InvokeVoidAsync("frame", cancellationToken, chunk, frame.Offset.TotalMilliseconds);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _watchBufferEscalationStateCts.CancelAndDisposeSilently();
        _watchBufferEscalationStateCts = null;
        var (jsRef, blazorRef) = (_jsRef, _blazorRef);
        _jsRef = null;
        await InvokeAsync(async () => {
            await jsRef.DisposeSilentlyAsync();
            blazorRef.DisposeSilently();
        });
    }

    // Private methods

    private async Task WatchBufferEscalationState(ChatId chatId, int initialEscalation, CancellationToken ct)
    {
        try {
            var computed = await Computed
                .Capture(() => ChatAudioUI.GetPlaybackBufferEscalation(chatId, ct), ct)
                .ConfigureAwait(false);
            var lastEscalation = initialEscalation;
            await foreach (var (value, _) in computed.Changes(ct).ConfigureAwait(false)) {
                if (value == lastEscalation)
                    continue;

                lastEscalation = value;
                if (_jsRef != null)
                    _ = _jsRef.InvokeVoidAsync("setBufferEscalation", ct, value);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            // Expected on dispose
        }
    }

    private Task InvokeAsync(Func<Task> workItem)
        => InvokeAsync(async () => { await workItem().ConfigureAwait(false); return true; });

    private Task<TResult?> InvokeAsync<TResult>(Func<Task<TResult?>> workItem)
    {
        try {
            return Dispatcher.InvokeAsync(workItem);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"[WebAudioPlaybackEngine #{{AudioTrackPlayerId}}] {nameof(InvokeAsync)} failed", id);
            throw;
        }
    }
}
