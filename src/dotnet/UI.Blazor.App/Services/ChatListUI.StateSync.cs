using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatListUI
{
    // All state sync logic should be here

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(InvalidateIsSelectedChatUnlisted),
            AsyncChain.From(PlayTuneOnNewMessages),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task InvalidateIsSelectedChatUnlisted(CancellationToken cancellationToken)
    {
        var cValueBase = await Computed
            .Capture(() => IsSelectedChatUnlistedInternal(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cValueBase.Changes(cancellationToken);
        await foreach (var cValue in changes.ConfigureAwait(false)) {
            if (_isSelectedChatUnlisted.Value == cValue.Value)
                continue;

            DebugLog?.LogDebug("InvalidateIsSelectedChatUnlisted: push");
            _isSelectedChatUnlisted.Value = cValue.Value;
        }
    }

    private async Task PlayTuneOnNewMessages(CancellationToken cancellationToken)
    {
        if (Hub.HostInfo().AppKind.IsMobile())
            return; // skip tune notifications for mobile MAUI

        var cChatInfoMap = await Computed
            .Capture(() => ListAllUnorderedRaw(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var previous = await cChatInfoMap.Use(cancellationToken).ConfigureAwait(false);
        var lastPlayedAt = CpuNow; // Skip tune after loading
        await foreach (var change in cChatInfoMap.Changes(cancellationToken).ConfigureAwait(false))
            await OnChange(change.Value).ConfigureAwait(false);
        return;

        async Task OnChange(IReadOnlyDictionary<ChatId, ChatInfo> chatInfoMap)
        {
            var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
            var otherChatInfoItems = chatInfoMap.Where(x => x.Key != selectedChatId);
            if (lastPlayedAt + MinNotificationInterval <= CpuNow)
                foreach (var (chatId, chatInfo) in otherChatInfoItems) {
                    var isAlreadyExists = previous.TryGetValue(chatId, out var prevChatInfo);
                    if (!isAlreadyExists) {
                        // notify on new chat
                        _ = TuneUI.Play(Tune.NotifyOnNewMessageInApp);
                        lastPlayedAt = CpuNow;
                        break;
                    }

                    var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
                    var hasNewUnreadMessages = prevChatInfo!.UnmutedUnreadCount < chatInfo.UnmutedUnreadCount;
                    var isLastMessageOwn = chatInfo.LastTextEntry?.AuthorId == ownAuthor?.Id;
                    if (!hasNewUnreadMessages || isLastMessageOwn)
                        continue;

                    // notify on new message from other authors
                    _ = TuneUI.Play(Tune.NotifyOnNewMessageInApp);
                    lastPlayedAt = CpuNow;
                    break;
                }
            previous = chatInfoMap;
        }
    }
}
