using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Services;

public partial class CallScreensUI
{
    private static readonly string JSStartRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.start";
    private static readonly string JSStopRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.stop";

    private int _ringGeneration;

    // Private methods

    private void StartRinging()
    {
        // Fire-and-forget, so SyncRingtone's finally can stop the ring synchronously.
        if (Bridge is not null)
            _ = StartNativeRinging(Interlocked.Increment(ref _ringGeneration));
        else
            _ = InvokeWebRingtone(JSStartRingtone);
    }

    private void StopRinging()
    {
        if (Bridge is not null) {
            // Bumped first: a start still waiting on the audio mode drops instead of ringing on.
            Interlocked.Increment(ref _ringGeneration);
            Bridge.StopRinging();
            _ = RestoreAudioMode();
        }
        else
            _ = InvokeWebRingtone(JSStopRingtone);
    }

    private async Task StartNativeRinging(int generation)
    {
        // The ringer stream follows the call route while the mode is InCommunication, so an armed
        // session holding it would put the whole ring in the earpiece. Nothing on the line - nothing
        // to protect: hand the mode back for the ring, exactly as a Normal-mode ring would sound.
        var liveChatIds = GetLiveAudioChatIds();
        Log.LogInformation("Incoming ring: live audio in [{ChatIds}]", liveChatIds.ToDelimitedString(","));
        if (liveChatIds.Count == 0) {
            try {
                await Hub.AudioFocusUI.YieldCommunicationMode().ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Couldn't yield the communication mode to the incoming ring");
            }
        }

        if (Volatile.Read(ref _ringGeneration) != generation)
            return;

        Bridge!.StartRinging();
    }

    private async Task RestoreAudioMode()
    {
        try {
            await Hub.AudioFocusUI.RestoreCommunicationMode().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't restore the communication mode after the incoming ring");
        }
    }

    private List<ChatId> GetLiveAudioChatIds()
        => Hub.ActiveChatsUI.ActiveChats.Value
            .Where(c => c.IsListening || c.IsRecording)
            .Select(c => c.ChatId)
            .ToList();

    private async Task InvokeWebRingtone(string jsMethod)
    {
        try {
            await Hub.JS.InvokeVoidAsync(jsMethod).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Web ringtone call {Method} failed", jsMethod);
        }
    }
}
