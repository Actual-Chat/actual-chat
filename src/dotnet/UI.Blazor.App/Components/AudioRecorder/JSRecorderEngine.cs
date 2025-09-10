using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class JSRecorderEngine(AppUIHub hub) : IAudioRecorderEngine
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.AudioRecorder.create";
    private IJSObjectReference _jsRef = null!;

    public async Task InitializeAsync(IAudioRecorderBackend backend, CancellationToken cancellationToken = default)
    {
        var js = hub.JS;
        var blazorRef = DotNetObjectReference.Create(backend);
        _jsRef = await js.InvokeAsync<IJSObjectReference>(JSCreateMethod, blazorRef).ConfigureAwait(false);
    }

    public async Task<bool> StartAsync(ChatId chatId, ChatEntryId? repliedChatEntryId, string sessionToken, CancellationToken cancellationToken = default)
        => await _jsRef.InvokeAsync<bool>("startRecording", CancellationToken.None, chatId, repliedChatEntryId, sessionToken)
            .AsTask()
            .WaitAsync(AudioRecorder.StartRecordingTimeout, cancellationToken)
            .ConfigureAwait(false);

    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        await _jsRef.InvokeVoidAsync("stopRecording", CancellationToken.None)
            .AsTask()
            .WaitAsync(AudioRecorder.StopRecordingTimeout, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
        => await _jsRef.InvokeVoidAsync("ensureConnected", CancellationToken.None, quickReconnect).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask ConversationSignal(CancellationToken cancellationToken)
        => await _jsRef.InvokeVoidAsync("conversationSignal", CancellationToken.None).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
        => await _jsRef.InvokeAsync<AudioRecorder.AudioDiagnosticsState>("runDiagnostics", CancellationToken.None).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

    public async Task<string?> CheckPermissionAsync(CancellationToken cancellationToken)
        => await _jsRef.InvokeAsync<string>("checkPermission", CancellationToken.None).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken)
        => await _jsRef.InvokeAsync<bool>("requestPermission", CancellationToken.None).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
}
