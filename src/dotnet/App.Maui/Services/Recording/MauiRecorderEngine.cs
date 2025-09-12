using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services.Recording;

public class MauiRecorderEngine(UIHub hub) : IAudioRecorderEngine
{
    [field: AllowNull, MaybeNull]
    public MicrophonePermissionHandler MicrophonePermissionHandler => field ??= hub.Services.GetRequiredService<MicrophonePermissionHandler>();

    public Task<bool> StartAsync(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        string sessionToken,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<bool> StopAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ConversationSignal(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        var permissionStatus = await MicrophonePermissionHandler.Check(cancellationToken);

        return new AudioRecorder.AudioDiagnosticsState {
            HasMicrophonePermission = permissionStatus,
        };
    }
}
