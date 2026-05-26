using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Plays audio file attachments via a single shared HTMLAudioElement.
/// One instance per UI scope; mutually exclusive with chat audio replay.
/// </summary>
public sealed class AudioAttachmentPlayer : UIServiceBase<AppUIHub>, IAsyncDisposable
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.AudioAttachmentPlayer.create";

    private readonly MutableState<PlaybackState?> _state;
    private readonly object _lock = new();
    private DotNetObjectReference<AudioAttachmentPlayer>? _blazorRef;
    private Task<IJSObjectReference>? _jsRefTask;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;

    public IState<PlaybackState?> State => _state;

    public AudioAttachmentPlayer(AppUIHub hub) : base(hub)
    {
        _state = StateFactory.NewMutable(
            (PlaybackState?)null,
            StateCategories.Get(GetType(), nameof(State)));
    }

    public async ValueTask DisposeAsync()
    {
        Task<IJSObjectReference>? jsRefTask;
        DotNetObjectReference<AudioAttachmentPlayer>? blazorRef;
        lock (_lock) {
            jsRefTask = _jsRefTask;
            blazorRef = _blazorRef;
            _jsRefTask = null;
            _blazorRef = null;
        }
        if (jsRefTask is not null) {
            try {
                var jsRef = await jsRefTask.ConfigureAwait(false);
                await jsRef.DisposeSilentlyAsync("dispose").ConfigureAwait(false);
            }
            catch {
                // ignored
            }
        }
        blazorRef?.DisposeSilently();
    }

    public async ValueTask Play(ChatEntryAttachment attachment)
    {
        if (!attachment.IsAudio())
            return;

        var url = UrlMapper.ContentUrl(attachment.Media.BlobId);
        var fileName = attachment.Media.FileName;
        var attachmentId = attachment.Id;

        var current = _state.Value;
        if (current is not null && current.AttachmentId == attachmentId) {
            await Resume().ConfigureAwait(true);
            return;
        }

        _state.Value = new PlaybackState {
            AttachmentId = attachmentId,
            Url = url,
            FileName = fileName,
            IsLoading = true,
            IsPlaying = true,
        };
        ChatAudioUI.StopReplay();
        var jsRef = await EnsureJSRef().ConfigureAwait(true);
        await jsRef.InvokeVoidAsync("play", url).ConfigureAwait(true);
    }

    public async ValueTask Pause()
    {
        var jsRef = TryGetJSRef();
        if (jsRef is null)
            return;

        await jsRef.InvokeVoidAsync("pause").ConfigureAwait(true);
    }

    public async ValueTask Resume()
    {
        var jsRef = TryGetJSRef();
        if (jsRef is null)
            return;

        ChatAudioUI.StopReplay();
        await jsRef.InvokeVoidAsync("resume").ConfigureAwait(true);
    }

    public async ValueTask Stop()
    {
        var jsRef = TryGetJSRef();
        if (jsRef is not null)
            await jsRef.InvokeVoidAsync("stop").ConfigureAwait(true);
        _state.Value = null;
    }

    public async ValueTask Seek(TimeSpan position)
    {
        var jsRef = TryGetJSRef();
        if (jsRef is null)
            return;

        await jsRef.InvokeVoidAsync("seek", position.TotalSeconds).ConfigureAwait(true);
    }

    public ValueTask SkipBy(TimeSpan delta)
    {
        var current = _state.Value;
        if (current is null)
            return ValueTask.CompletedTask;

        var target = current.Position + delta;
        if (target < TimeSpan.Zero)
            target = TimeSpan.Zero;
        if (current.Duration is { } d && target > d)
            target = d;
        return Seek(target);
    }

    // JS callbacks

    [JSInvokable]
    public void OnTimeUpdate(double positionSec)
    {
        var current = _state.Value;
        if (current is null)
            return;

        _state.Value = current with { Position = TimeSpan.FromSeconds(positionSec) };
    }

    [JSInvokable]
    public void OnDurationChange(double durationSec)
    {
        var current = _state.Value;
        if (current is null || double.IsNaN(durationSec) || double.IsInfinity(durationSec))
            return;

        _state.Value = current with { Duration = TimeSpan.FromSeconds(durationSec), IsLoading = false };
    }

    [JSInvokable]
    public void OnPlay()
    {
        var current = _state.Value;
        if (current is null)
            return;

        _state.Value = current with { IsPlaying = true };
    }

    [JSInvokable]
    public void OnPause()
    {
        var current = _state.Value;
        if (current is null)
            return;

        _state.Value = current with { IsPlaying = false };
    }

    [JSInvokable]
    public void OnEnded()
    {
        var current = _state.Value;
        if (current is null)
            return;

        _state.Value = current with {
            IsPlaying = false,
            Position = current.Duration ?? current.Position,
        };
    }

    [JSInvokable]
    public void OnError(string message)
    {
        Log.LogWarning("Audio playback error: {Message}", message);
        _state.Value = null;
    }

    // Private methods

    private IJSObjectReference? TryGetJSRef()
    {
        Task<IJSObjectReference>? task;
        lock (_lock)
            task = _jsRefTask;
        if (task is null || !task.IsCompletedSuccessfully)
            return null;

        return task.Result;
    }

    private Task<IJSObjectReference> EnsureJSRef()
    {
        lock (_lock) {
            if (_jsRefTask is not null)
                return _jsRefTask;

            _blazorRef = DotNetObjectReference.Create(this);
            _jsRefTask = JS.InvokeAsync<IJSObjectReference>(JSCreateMethod, _blazorRef).AsTask();
            return _jsRefTask;
        }
    }
}

public sealed record PlaybackState
{
    public required Symbol AttachmentId { get; init; }
    public required string Url { get; init; }
    public required string FileName { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan? Duration { get; init; }
    public bool IsPlaying { get; init; }
    public bool IsLoading { get; init; }
}
