using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI
{
    // Well inside LiveVideoBackend.MemberStalenessThreshold (90s), so a member
    // never ages out mid-call.
    private static readonly TimeSpan MemberRegistrationPeriod = TimeSpan.FromSeconds(20);
    private static readonly string JSGetSupportedDecoderCodecsMethod =
        $"{BlazorUIAppModule.ImportName}.getSupportedDecoderCodecs";

    // Registration follows video-session membership, not the camera. What is
    // registered is DECODE capability, which is a property of viewers: a
    // participant watching with their camera off is exactly who the negotiation
    // protects, and tying this to the recorder would leave them invisible while
    // the senders upgrade to a codec they cannot play.
    private async Task SyncMemberRegistration(CancellationToken cancellationToken)
    {
        var cActiveChat = await Computed
            .Capture(() => GetActiveVideoChatId(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        var cpuClock = Clocks.CpuClock;
        ChatId? registeredChatId = null;
        var registeredAt = default(Moment);
        try {
            while (!cancellationToken.IsCancellationRequested) {
                cActiveChat = await cActiveChat.Update(cancellationToken).ConfigureAwait(false);
                var activeChatId = cActiveChat.Value;
                if (registeredChatId is { } previous && previous != activeChatId) {
                    await UnregisterMember(previous).ConfigureAwait(false);
                    registeredChatId = null;
                }
                if (activeChatId is not { } chatId) {
                    await cActiveChat.When(x => x is not null, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // GetActiveVideoChatId is invalidated far more often than the
                // registration changes, so re-register only when the chat is
                // new or the heartbeat is due — otherwise this is an RPC every
                // few seconds for the life of the call.
                var isDue = registeredChatId != chatId
                    || cpuClock.Now - registeredAt >= MemberRegistrationPeriod;
                if (isDue) {
                    var codecs = await JS
                        .InvokeAsync<string[]>(JSGetSupportedDecoderCodecsMethod, cancellationToken)
                        .ConfigureAwait(false);
                    await LiveVideoStreams
                        .RegisterMember(Session, chatId, new ApiArray<string>(codecs), cancellationToken)
                        .ConfigureAwait(false);
                    if (registeredChatId != chatId)
                        Log.LogInformation("SyncMemberRegistration({ChatId}): decoder codecs=[{Codecs}]",
                            chatId, string.Join(", ", codecs));
                    registeredChatId = chatId;
                    registeredAt = cpuClock.Now;
                }

                // Re-registers on a timer rather than only on change: the server
                // drops members that go stale, and an invalidation lost anywhere
                // along the chain would otherwise never heal.
                using var waitCts = cancellationToken.CreateLinkedTokenSource();
                try {
                    await cActiveChat.WhenInvalidated(waitCts.Token)
                        .WaitAsync(MemberRegistrationPeriod, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException) { }
                finally {
                    waitCts.CancelAndDisposeSilently();
                }
            }
        }
        finally {
            // Leaving a member behind would keep narrowing the codec set for
            // everyone still in the call.
            if (registeredChatId is { } last)
                await UnregisterMember(last).ConfigureAwait(false);
        }
    }

    private async Task UnregisterMember(ChatId chatId)
    {
        try {
            await LiveVideoStreams.UnregisterMember(Session, chatId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "UnregisterMember({ChatId}) failed", chatId);
        }
    }
}
