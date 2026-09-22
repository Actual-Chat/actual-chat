using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Services;

public partial class CallScreensUI
{
    private static readonly string JSStartRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.start";
    private static readonly string JSStopRingtone = $"{BlazorUIAppModule.ImportName}.IncomingCallRingtone.stop";
    private static readonly string JSStartRingback = $"{BlazorUIAppModule.ImportName}.OutgoingCallRingback.start";
    private static readonly string JSStopRingback = $"{BlazorUIAppModule.ImportName}.OutgoingCallRingback.stop";

    private int _ringGeneration;

    // Private methods

    private void StartRinging()
    {
        // Fire-and-forget, so SyncRingtone's finally can stop the ring synchronously.
        if (Bridge is null) {
            _ = InvokeSound(JSStartRingtone);
            return;
        }

        // OwnsRinging (CallKit) rings itself right away; everyone else negotiates the
        // communication audio mode first via StartNativeRinging.
        if (Bridge.OwnsRinging)
            Bridge.StartRinging();
        else
            _ = StartNativeRinging(Interlocked.Increment(ref _ringGeneration));
    }

    private void StopRinging(bool mustEndOwnedRing = true)
    {
        if (Bridge is null) {
            _ = InvokeSound(JSStopRingtone);
            return;
        }

        // Bumped first: a start still waiting on the audio mode drops instead of ringing on.
        Interlocked.Increment(ref _ringGeneration);
        // An owned ring (CallKit) is the call itself and can't be re-reported once ended, so only
        // an actual ring end may take it down; everyone else must always stop their ringer.
        if (mustEndOwnedRing || !Bridge.OwnsRinging)
            Bridge.StopRinging();
        if (!Bridge.OwnsRinging)
            _ = RestoreAudioMode();
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

    private async Task SyncRingback(CancellationToken cancellationToken)
    {
        // Follows the server's dialing, not the slot's: the slot is claimed before the StartCall RPC, and a
        // refused call must not ring back first.
        var cDialing = await Computed
            .Capture(() => CallUI.GetDialingOutChatId(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var isRingbackOn = false;
        try {
            await foreach (var c in cDialing.Changes(cancellationToken).ConfigureAwait(false)) {
                var mustPlay = c.Value is not null;
                if (mustPlay == isRingbackOn)
                    continue;

                isRingbackOn = mustPlay;
                _ = InvokeSound(mustPlay ? JSStartRingback : JSStopRingback);
            }
        }
        finally {
            if (isRingbackOn)
                _ = InvokeSound(JSStopRingback);
        }
    }

    private async Task InvokeSound(string jsMethod)
    {
        try {
            await Hub.JS.InvokeVoidAsync(jsMethod).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Call sound {Method} failed", jsMethod);
        }
    }
}
