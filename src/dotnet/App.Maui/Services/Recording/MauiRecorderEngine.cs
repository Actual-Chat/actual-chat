using ActualChat.UI.Blazor.App.Components;
using Microsoft.Maui.ApplicationModel;

namespace ActualChat.App.Maui.Services.Recording;

public class MauiRecorderEngine : IAudioRecorderEngine
{
    private IAudioRecorderBackend _backend;

    public Task InitializeAsync(IAudioRecorderBackend backend, CancellationToken cancellationToken = default)
    {
        _backend = backend;
        return Task.CompletedTask;
    }

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

    public async Task<string?> CheckPermissionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
            return status switch
            {
                PermissionStatus.Granted => "granted",
                PermissionStatus.Denied => "denied",
                PermissionStatus.Restricted => "restricted",
                _ => "prompt",
            };
        }
        catch
        {
            return "unknown";
        }
    }

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await Permissions.RequestAsync<Permissions.Microphone>();
            return status == PermissionStatus.Granted;
        }
        catch
        {
            return false;
        }
    }

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        var permissionStatus = await CheckPermissionAsync(cancellationToken);
        var hasPermission = permissionStatus == "granted";

        return new AudioRecorder.AudioDiagnosticsState {
            HasMicrophonePermission = hasPermission,
        };
    }
}
