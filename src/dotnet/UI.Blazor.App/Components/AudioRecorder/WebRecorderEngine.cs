using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class WebRecorderEngine(AppUIHub hub) : IAudioRecorderEngine
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.AudioRecorder.create";
    private IJSObjectReference _jsRef = null!;
    private AppUIHub Hub { get; } = hub;

    public async Task<RecorderStartResult> Start(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitialized(cancellationToken).ConfigureAwait(false);

        var isStarted = await _jsRef.InvokeAsync<bool>("startRecording",
                CancellationToken.None,
                chatId,
                repliedChatEntryId)
            .AsTask()
            .WaitAsync(AudioRecorder.StartRecordingTimeout, cancellationToken)
            .ConfigureAwait(false);
        // The JS recorder gives back a bare false, and on the web that's getUserMedia
        // denying access far more often than a device that won't open
        return isStarted ? RecorderStartResult.Started : RecorderStartResult.NoPermission;
    }

    public async Task<bool> Stop(CancellationToken cancellationToken = default)
    {
        await EnsureInitialized(cancellationToken).ConfigureAwait(false);

        await _jsRef.InvokeVoidAsync("stopRecording", CancellationToken.None)
            .AsTask()
            .WaitAsync(AudioRecorder.StopRecordingTimeout, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async ValueTask ConversationSignal(CancellationToken cancellationToken)
    {
        await EnsureInitialized(cancellationToken).ConfigureAwait(false);

        await _jsRef.InvokeVoidAsync("conversationSignal", CancellationToken.None)
            .AsTask()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        await EnsureInitialized(cancellationToken).ConfigureAwait(false);

        return await _jsRef.InvokeAsync<AudioRecorder.AudioDiagnosticsState>("runDiagnostics", CancellationToken.None)
            .AsTask()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private async ValueTask EnsureInitialized(CancellationToken cancellationToken = default)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (_jsRef != null)
            return;

        var js = Hub.JS;
        var backend = Hub.Services.GetRequiredService<IAudioRecorderBackend>();
        var blazorRef = DotNetObjectReference.Create(backend);
        _jsRef = await js
            .InvokeAsync<IJSObjectReference>(JSCreateMethod, cancellationToken, blazorRef)
            .ConfigureAwait(false);
    }
}
