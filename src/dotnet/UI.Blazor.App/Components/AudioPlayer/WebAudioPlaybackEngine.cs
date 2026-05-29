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

    private Dispatcher Dispatcher => field ??= services.GetRequiredService<Dispatcher>();

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

        var targetBufferSizeMs = trackInfo.TargetBufferSize.TotalMilliseconds;

        Log.LogDebug(
            "[WebAudioPlaybackEngine #{AudioTrackPlayerId}] Play: authorId={AuthorId}, recordedAtMs={RecordedAtMs}, targetBufferSizeMs={TargetBufferSizeMs}",
            id, authorId, recordedAtMs, targetBufferSizeMs);

        var js = services.JSRuntime();
        var title = author?.Avatar.Name ?? "";
        var album = chat?.Title ?? "";
        var whenPlayerCreated = js.InvokeAsync<IJSObjectReference>(JSCreateMethod,
            CancellationToken.None,
            _blazorRef, id, preSkip, title, album, authorId, recordedAtMs, targetBufferSizeMs);
        _whenPlayerCreated = whenPlayerCreated.AsTask();
        try {
            _jsRef = await whenPlayerCreated.ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e,
                "[WebAudioPlaybackEngine #{AudioTrackPlayerId}] JS AudioPlayer.create threw — feeder/decoder may be in a broken state; only page refresh recovers (suspect: AudioContextSource trait attachment poisoning)",
                id);
            throw;
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

    public ValueTask SkipUntil(TimeSpan sourceOffset, CancellationToken cancellationToken)
    {
        if (_jsRef == null)
            throw StandardError.StateTransition(GetType(), "Can't skip before initialization.");

        _ = _jsRef.InvokeVoidAsync("skipUntil", cancellationToken, sourceOffset.TotalMilliseconds);
        return ValueTask.CompletedTask;
    }

    public ValueTask SpeedUpUntil(TimeSpan sourceOffset, int dropEveryNFrames, CancellationToken cancellationToken)
    {
        if (_jsRef == null)
            throw StandardError.StateTransition(GetType(), "Can't speed up before initialization.");

        _ = _jsRef.InvokeVoidAsync(
            "speedUpUntil",
            cancellationToken,
            sourceOffset.TotalMilliseconds,
            dropEveryNFrames);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetTargetBufferSize(TimeSpan targetBufferSize, CancellationToken cancellationToken)
    {
        // No-op before the JS player exists; the start-time hold seeds the value.
        if (_jsRef == null)
            return ValueTask.CompletedTask;

        _ = _jsRef.InvokeVoidAsync("setTargetBufferSize", cancellationToken, targetBufferSize.TotalMilliseconds);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        var (jsRef, blazorRef) = (_jsRef, _blazorRef);
        _jsRef = null;
        await InvokeAsync(async () => {
            await jsRef.DisposeSilentlyAsync();
            blazorRef.DisposeSilently();
        });
    }

    // Private methods

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
