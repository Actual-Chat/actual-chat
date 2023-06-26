﻿using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Hosting;
using Stl.Locking;

namespace ActualChat.Audio.UI.Blazor.Components;

public class AudioRecorder : IAsyncDisposable
{
    private static readonly TimeSpan StartRecordingTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StopRecordingTimeout = TimeSpan.FromSeconds(3);

    private readonly AsyncLock _stateLock = AsyncLock.New(LockReentryMode.Unchecked);
    private readonly IMutableState<AudioRecorderState> _state;
    private IJSObjectReference _jsRef = null!;

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.AudioRecording;

    private Session Session { get; }
    private IJSRuntime JS { get; }
    private IServiceProvider Services { get; }

    public MicrophonePermissionHandler MicrophonePermission { get; }
    public IState<AudioRecorderState> State => _state;
    public Task WhenInitialized { get; }


    public AudioRecorder(IServiceProvider services)
    {
        Log = services.LogFor<AudioRecorder>();
        Session = services.GetRequiredService<Session>();
        JS = services.GetRequiredService<IJSRuntime>();
        MicrophonePermission = services.GetRequiredService<MicrophonePermissionHandler>();
        Services = services;

        _state = services.StateFactory().NewMutable(
            AudioRecorderState.Idle,
            StateCategories.Get(GetType(), nameof(State)));
        WhenInitialized = Initialize();
        return;

        async Task Initialize()
        {
            var hostInfo = services.GetRequiredService<HostInfo>();
            // TODO(AK): register recorderId for the session
            var recorderId = hostInfo is { Platform: Platform.iOS, AppKind: AppKind.MauiApp }
                ? Session.Id.Value
                : Constants.Recorder.DefaultId;
            _jsRef = await JS.InvokeAsync<IJSObjectReference>(
                    $"{AudioBlazorUIModule.ImportName}.AudioRecorder.create",
                    recorderId)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var _ = await _stateLock.Lock().ConfigureAwait(false);
        await _jsRef.DisposeSilentlyAsync("dispose").ConfigureAwait(false);
        _jsRef = null!;
    }

    public async Task StartRecording(
        ChatId chatId,
        ChatEntryId repliedChatEntryId,
        CancellationToken cancellationToken = default)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        var audioInitializer = Services.GetRequiredService<AudioInitializer>();
        await audioInitializer.WhenInitialized.ConfigureAwait(false);

        using var _ = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        var state = _state.Value;
        if (state.ChatId == chatId) {
            if (state.IsRecording)
                return; // Already started
        }
        else if (!state.ChatId.IsNone)
            await StopRecordingUnsafe();

        MarkStarting(chatId);
        var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(StartRecordingTimeout);
        try {
            var isStarted = await _jsRef
                .InvokeAsync<bool>("startRecording", cts.Token, chatId, repliedChatEntryId)
                .ConfigureAwait(false);
            if (!isStarted) {
                MicrophonePermission.Reset();
                Log.LogWarning(nameof(StartRecording) + ": chat #{ChatId} - can't access the microphone", chatId);
                // Cancel recording
                MarkStopped();
                throw new AudioRecorderException(
                    "Can't access the microphone - please check if microphone access permission is granted.");
            }
            MarkStarted();
        }
        catch (Exception e) when (e is not AudioRecorderException) {
            if (e is not OperationCanceledException)
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                DebugLog?.LogDebug(nameof(StartRecording) + " is cancelled");
            else
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                Log.LogError(e, nameof(StartRecording) + " failed");

            await StopRecordingUnsafe().ConfigureAwait(false);

            if (e is OperationCanceledException) {
                if (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    throw new AudioRecorderException("Failed to start recording in time.", e);
                throw;
            }
            throw new AudioRecorderException("Failed to start recording.", e);
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    public async Task<bool> StopRecording(CancellationToken cancellationToken = default)
    {
        await WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var _ = await _stateLock.Lock(cancellationToken).ConfigureAwait(false);
        return await StopRecordingUnsafe().ConfigureAwait(false);
    }

    // Private methods

    private async Task<bool> StopRecordingUnsafe()
    {
        var chatId = _state.Value.ChatId;
        if (chatId.IsNone || _jsRef == null!)
            return true; // Nothing to do

        // This method should reliably stop the recording, so we don't use normal cancellation here
        var cts = new CancellationTokenSource(StopRecordingTimeout);
        try {
            await _jsRef.InvokeVoidAsync("stopRecording", cts.Token).ConfigureAwait(false);
        }
        catch (JSDisconnectedException) { } // Circuit is disposed or disposing
        catch (ObjectDisposedException) { } // Circuit is disposed or disposing
        catch (Exception e) {
            var reason = cts.IsCancellationRequested ? "timed out" : "failed";
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogError(e, $"{nameof(StopRecordingUnsafe)}: chat #{{ChatId}} - {reason}, recorder state is in doubt", chatId);
            return false;
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
        MarkStopped();
        return true;
    }

    internal async Task<bool> RequestPermission(CancellationToken cancellationToken = default)
    {
        await WhenInitialized.ConfigureAwait(false);
        return await _jsRef.InvokeAsync<bool>("requestPermission", cancellationToken).ConfigureAwait(false);
    }

    // MarkXxx

    private void MarkStarting(ChatId chatId)
    {
        _state.Value = new AudioRecorderState(chatId);
        DebugLog?.LogDebug("Chat #{ChatId}: recording is starting", chatId);
    }

    private void MarkStarted()
    {
        var state = _state.Value;
        if (state.ChatId.IsNone)
            throw StandardError.Internal("Something is off: MarkStarted() is called, but ChatId.IsNone == true.");

        _state.Value = state with { IsRecording = true };
        DebugLog?.LogDebug("Chat #{ChatId}: recording is started", state.ChatId);
    }

    private void MarkStopped()
    {
        _state.Value = AudioRecorderState.Idle;
        DebugLog?.LogDebug("Recording is stopped");
    }
}
