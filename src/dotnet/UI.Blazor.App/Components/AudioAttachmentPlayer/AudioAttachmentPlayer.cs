using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Resources;

// ReSharper disable once CheckNamespace
namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Plays audio file attachments via a single shared HTMLAudioElement.
/// One instance per UI scope; mutually exclusive with chat audio replay.
/// </summary>
public sealed class AudioAttachmentPlayer : UIServiceBase<AppUIHub>, IAsyncDisposable
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.AudioAttachmentPlayer.create";

    private readonly MutableState<PlaybackState?> _state;
    private readonly Lock _lock = new();
    private DotNetObjectReference<AudioAttachmentPlayer>? _blazorRef;
    private Task<IJSObjectReference>? _jsRefTask;
    private volatile bool _isDisposed;
    private ImmutableHashSet<ChatId> _listeningChatsBeforeAudio = ImmutableHashSet<ChatId>.Empty;

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
            _isDisposed = true;
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
        if (_isDisposed || !attachment.IsAudio())
            return;

        var url = UrlMapper.ContentUrl(attachment.Media.BlobId);
        var fileName = attachment.Media.FileName;
        var attachmentId = attachment.Id;

        var current = _state.Value;
        if (current is not null && current.AttachmentId == attachmentId) {
            await Resume().ConfigureAwait(true);
            return;
        }

        if (!await TryConfirmInterruptConversation().ConfigureAwait(true))
            return;

        _state.Value = new PlaybackState {
            AttachmentId = attachmentId,
            Url = url,
            FileName = fileName,
            IsLoading = true,
            IsPlaying = true,
        };
        try {
            ChatAudioUI.StopReplay();
            var jsRef = await EnsureJSRef().ConfigureAwait(true);
            await jsRef.InvokeVoidAsync("play", url).ConfigureAwait(true);
        }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask Pause()
    {
        if (_isDisposed)
            return;
        var jsRef = TryGetJSRef();
        if (jsRef is null)
            return;

        try { await jsRef.InvokeVoidAsync("pause").ConfigureAwait(true); }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask Resume()
    {
        if (_isDisposed)
            return;
        var jsRef = TryGetJSRef();
        if (jsRef is null)
            return;

        if (!await TryConfirmInterruptConversation().ConfigureAwait(true))
            return;

        try {
            ChatAudioUI.StopReplay();
            await jsRef.InvokeVoidAsync("resume").ConfigureAwait(true);
        }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask Stop()
    {
        if (_isDisposed)
            return;
        var jsRef = TryGetJSRef();
        if (jsRef is not null) {
            try { await jsRef.InvokeVoidAsync("stop").ConfigureAwait(true); }
            catch (ObjectDisposedException) { }
        }
        _state.Value = null;
        await RestoreConversation().ConfigureAwait(true);
    }

    public async ValueTask Seek(TimeSpan position)
    {
        if (_isDisposed)
            return;
        var jsRef = TryGetJSRef();
        if (jsRef is null)
            return;

        try { await jsRef.InvokeVoidAsync("seek", position.TotalSeconds).ConfigureAwait(true); }
        catch (ObjectDisposedException) { }
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
        if (_isDisposed)
            return;
        var current = _state.Value;
        if (current is null)
            return;

        _state.Value = current with { Position = TimeSpan.FromSeconds(positionSec) };
    }

    [JSInvokable]
    public void OnDurationChange(double durationSec)
    {
        if (_isDisposed)
            return;
        var current = _state.Value;
        if (current is null || double.IsNaN(durationSec) || double.IsInfinity(durationSec))
            return;

        _state.Value = current with { Duration = TimeSpan.FromSeconds(durationSec), IsLoading = false };
    }

    [JSInvokable]
    public void OnPlay()
    {
        if (_isDisposed)
            return;
        var current = _state.Value;
        if (current is null)
            return;

        _state.Value = current with { IsPlaying = true };
    }

    [JSInvokable]
    public void OnPause()
    {
        if (_isDisposed)
            return;
        var current = _state.Value;
        if (current is null)
            return;

        _state.Value = current with { IsPlaying = false };
    }

    [JSInvokable]
    public void OnEnded()
    {
        if (_isDisposed)
            return;
        var current = _state.Value;
        if (current is null)
            return;

        _state.Value = current with {
            IsPlaying = false,
            Position = current.Duration ?? current.Position,
        };
        _ = BackgroundTask.Run(RestoreConversation, Log, "RestoreConversation failed", CancellationToken.None);
    }

    [JSInvokable]
    public void OnError(string message)
    {
        if (_isDisposed)
            return;
        Log.LogWarning("Audio playback error: {Message}", message);
        _state.Value = null;
        _ = BackgroundTask.Run(RestoreConversation, Log, "RestoreConversation failed", CancellationToken.None);
    }

    internal void OnConversationJoined()
    {
        if (_isDisposed)
            return;
        _listeningChatsBeforeAudio = ImmutableHashSet<ChatId>.Empty;
        if (_state.Value is { IsPlaying: true })
            _ = BackgroundTask.Run(() => Pause().AsTask(), Log, "Pause failed", CancellationToken.None);
    }

    // Private methods

    private async Task<bool> TryConfirmInterruptConversation()
    {
        var activeChats = Hub.ActiveChatsUI.ActiveChats.Value
            .Where(c => c.IsListening || c.IsRecording)
            .ToList();
        if (activeChats.Count == 0)
            return true;

        var text = activeChats.Count == 1
            ? L.AudioPlayer_InterruptOne
            : L.AudioPlayer_InterruptMany_Format(activeChats.Count);
        var confirmed = false;
        var modalRef = await Hub.ModalUI.Show(new ConfirmModal.Model(
                IsDestructive: false,
                text,
                Confirm: () => confirmed = true) {
                Title = L.AudioPlayer_InterruptTitle,
                ConfirmButtonText = L.AudioPlayer_Confirm,
                CancelButtonText = L.Common_Cancel,
            })
            .ConfigureAwait(true);
        await modalRef.WhenClosed.ConfigureAwait(true);
        if (!confirmed)
            return false;

        _listeningChatsBeforeAudio = activeChats
            .Where(c => c.IsListening)
            .Select(c => c.ChatId)
            .ToImmutableHashSet();
        await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(true);
        await ChatAudioUI.ClearListeningChats().ConfigureAwait(true);
        return true;
    }

    private async Task RestoreConversation()
    {
        var toRestore = _listeningChatsBeforeAudio;
        _listeningChatsBeforeAudio = ImmutableHashSet<ChatId>.Empty;
        foreach (var chatId in toRestore)
            await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(true);
    }

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
            ObjectDisposedException.ThrowIf(_isDisposed, this);
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
