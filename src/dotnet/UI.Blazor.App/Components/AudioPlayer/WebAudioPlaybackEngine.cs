using ActualChat.Audio;
using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

internal sealed class WebAudioPlaybackEngine(
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

    [field: AllowNull, MaybeNull]
    private Dispatcher Dispatcher => field ??= services.GetRequiredService<Dispatcher>();

    [field: AllowNull, MaybeNull]
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
        var js = services.JSRuntime();
        var whenPlayerCreated = js.InvokeAsync<IJSObjectReference>(JSCreateMethod,
            CancellationToken.None,
            _blazorRef, id, preSkip, author.Avatar.Name, chat.Title);
        _whenPlayerCreated = whenPlayerCreated.AsTask();
        _jsRef = await whenPlayerCreated.ConfigureAwait(false);
    }

    public async Task Pause(CancellationToken cancellationToken)
    {
        if (_jsRef == null && _whenPlayerCreated != null)
            await _whenPlayerCreated.ConfigureAwait(false);
        if (_jsRef == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");
        _ = _jsRef.InvokeVoidAsync("pause", CancellationToken.None)
            .Catch(Log, "Failed to invoke js player.pause()")
            .SuppressCancellationAwait();
    }

    public async Task Resume(CancellationToken cancellationToken)
    {
        if (_jsRef == null && _whenPlayerCreated != null)
            await _whenPlayerCreated.ConfigureAwait(false);
        if (_jsRef == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");
        _ = _jsRef.InvokeVoidAsync("resume", CancellationToken.None)
            .Catch(Log, "Failed to invoke js player.resume()")
            .SuppressCancellationAwait();
    }

    public async Task End(bool abort, CancellationToken cancellationToken)
    {
        if (_jsRef == null && _whenPlayerCreated != null)
            await _whenPlayerCreated.ConfigureAwait(false);
        if (_jsRef == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");
        _ = _jsRef.InvokeVoidAsync("end", CancellationToken.None, abort)
            .Catch(Log, $"Failed to invoke js player.end({abort.ToString().ToLowerInvariant()})")
            .SuppressCancellationAwait();
    }

    public Task Frame(MediaFrame frame, CancellationToken cancellationToken)
    {
        if (_jsRef == null)
            throw StandardError.StateTransition(GetType(), "Can't process media frame before initialization.");

        var chunk = frame.Data;
        _ = _jsRef.InvokeVoidAsync("frame", cancellationToken, chunk)
            .Catch(Log, "Failed to invoke js player.frame()")
            .SuppressCancellationAwait(false);

        return Task.CompletedTask;
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

