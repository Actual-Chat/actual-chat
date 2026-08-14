using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.Resources;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatUI
{
    private static readonly TimeSpan PrefetchThrottle = TimeSpan.FromSeconds(5);

    // All state sync logic should be here

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        // All logic here can be delayed to let other code run
        await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken).ConfigureAwait(false);
        var baseChains = new[] {
            AsyncChain.From(InvalidateSelectedChatDependencies),
            AsyncChain.From(NavigateToFixedSelectedChat),
            AsyncChain.From(ResetHighlightedEntry),
            AsyncChain.From(PushKeepAwakeState),
            AsyncChain.From(SynchronizeSelectedChatIdAndActivePlaceId),
            AsyncChain.From(PrefetchChatTails),
            AsyncChain.From(MonitorDetectedLanguage),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        await (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task InvalidateSelectedChatDependencies(CancellationToken cancellationToken)
    {
        var oldChatId = (ChatId?)null;
        var changes = SelectedChatId.Computed.ChangesUntyped(cancellationToken);
        await foreach (var c in changes.ConfigureAwait(false)) {
            var cSelectedContactId = (Computed<ChatId>)c;
            var newChatId = cSelectedContactId.Value;
            if (newChatId == oldChatId)
                continue;

            DebugLog?.LogDebug("InvalidateSelectedChatDependencies: *");
            using (Invalidation.Begin()) {
                if (oldChatId is not null) {
                    _ = IsSelected(oldChatId);
                    _ = IsSelected(oldChatId.GetThreadOutermostParentOrSelf());
                }
                _ = IsSelected(newChatId);
                _ = IsSelected(newChatId.GetThreadOutermostParentOrSelf());
            }

            SelectionUI.Clear();
            _ = ChatEditorUI.RestoreRelatedEntry(newChatId).ConfigureAwait(false);
            _ = UIEventHub.Publish<SelectedChatChangedEvent>(CancellationToken.None);
            _ = UICommander.RunNothing();
            oldChatId = newChatId;
        }
    }

    [ComputeMethod]
    protected virtual async Task<ChatId?> GetFixedSelectedChatId(CancellationToken cancellationToken)
    {
        var chatId = await SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        var fixedChatId = await FixChatId(chatId, cancellationToken).ConfigureAwait(false);
        var wasFixed = fixedChatId != chatId;
        return wasFixed ? fixedChatId : null;
    }

    private async Task NavigateToFixedSelectedChat(CancellationToken cancellationToken)
    {
        var cFixedSelectedChatId = await Computed
            .Capture(() => GetFixedSelectedChatId(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        cFixedSelectedChatId = await cFixedSelectedChatId
            .When(x => x is not null, cancellationToken)
            .ConfigureAwait(false);

        var link = Links.Chat(cFixedSelectedChatId.Value!);
        _ = AutoNavigationUI.DispatchNavigateTo(link, AutoNavigationReason.FixedChatId);
    }

    [ComputeMethod]
    protected virtual async Task<bool> MustKeepAwake(CancellationToken cancellationToken)
    {
        var activeChats = await ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        foreach (var chat in activeChats) {
            if (chat.IsListening || chat.IsRecording)
                return true;
            // Own video stream (camera or screencast) also requires keep-awake:
            // backgrounded / unfocused tabs see HW encoder priority drops that
            // degrade per-call latency 3-5× — KeepAwakeUI's wake lock hints the
            // OS/browser to maintain media priority.
            var ownVideoSourceKind = await Hub.ChatVideoUI.GetOwnSourceKind(chat.ChatId, cancellationToken).ConfigureAwait(false);
            if (ownVideoSourceKind != null)
                return true;
        }
        return false;
    }

    [ComputeMethod]
    protected virtual async Task<UserChatRecordingDetectedLanguage> GetDetectedLanguage(CancellationToken cancellationToken)
        => await UserSettingsUI.UserChatRecordingDetectedLanguage().Get(cancellationToken).ConfigureAwait(false);

    private async Task PushKeepAwakeState(CancellationToken cancellationToken)
    {
        var lastMustKeepAwake = (bool?)null;
        var cMustKeepAwake0 = await Computed
            .Capture(() => MustKeepAwake(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cMustKeepAwake0.Changes(FixedDelayer.Get(1), cancellationToken);
        await foreach (var cMustKeepAwake in changes.ConfigureAwait(false)) {
            var mustKeepAwake = cMustKeepAwake.Value;
            if (mustKeepAwake != lastMustKeepAwake) {
                await KeepAwakeUI.SetKeepAwake(mustKeepAwake).ConfigureAwait(false);
                lastMustKeepAwake = mustKeepAwake;
            }
        }
    }

    private async Task ResetHighlightedEntry(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts = null;
        try {
            // ReSharper disable once PossiblyMistakenUseOfCancellationToken
            var changes = HighlightedEntryId.Computed.ChangesUntyped(FixedDelayer.Get(0.1), cancellationToken);
            await foreach (var c in changes.ConfigureAwait(false)) {
                var cHighlightedEntryId = (Computed<ChatEntryId?>)c;
                cts.CancelAndDisposeSilently();
                var highlightedEntryId = cHighlightedEntryId.Value;
                if (highlightedEntryId is null)
                    continue; // Nothing to reset

                cts = cancellationToken.CreateLinkedTokenSource();
                var ctsToken = cts.Token;
                _ = BackgroundTask.Run(async () => {
                    await Task.Delay(TimeSpan.FromSeconds(2), ctsToken).ConfigureAwait(false);
                    if (HighlightedEntryId.Value == highlightedEntryId)
                        HighlightEntry(null, false);
                }, CancellationToken.None);
            }
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    private async Task SynchronizeSelectedChatIdAndActivePlaceId(CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        _ = SynchronizeSelectedChatIds();
    }

    private async Task SynchronizeSelectedChatIds()
    {
        await _selectedChatIds.WhenRead.ConfigureAwait(false);
        lock (Lock) {
            var selectedChatIds = _selectedChatIds.Value;
            if (_pendingSelectedChatIds is { Count: > 0 }) {
                // Selected chat ids are not loaded yet.
                foreach (var chatId in _pendingSelectedChatIds)
                    selectedChatIds = selectedChatIds.SetItem((chatId as PlaceChatId)?.PlaceId.Value ?? "", chatId);
                _selectedChatIds.Value = selectedChatIds;
            }
            _pendingSelectedChatIds = null;
        }
    }

    private async Task MonitorDetectedLanguage(CancellationToken cancellationToken)
    {
        var lastDetectedLanguageChatId = (ChatId?)null;
        var cDetected = await Computed
            .Capture(() => GetDetectedLanguage(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cDetected.Changes(FixedDelayer.Get(0.5), cancellationToken);
        await foreach (var c in changes.ConfigureAwait(false)) {
            var detected = c.Value;
            if (detected.ChatId is null || detected.Language is null)
                continue;

            var chatId = detected.ChatId!;
            if (chatId == lastDetectedLanguageChatId)
                continue;

            var data = await LanguageUI.GetChatLanguageAndPrimary(chatId, cancellationToken).ConfigureAwait(false);
            if (data.Item1 is not null)
                continue; // language already set explicitly

            if (!IsRecentDetection(detected.Timestamp))
                continue;

            lastDetectedLanguageChatId = chatId;
            _ = BackgroundTask.Run(
                () => ApplyDetectedLanguage(chatId, detected.Language, cancellationToken),
                cancellationToken);
        }
    }

    private async Task ApplyDetectedLanguage(ChatId chatId, Language language,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        // Wait for a pause in speech before applying the language change
        var audioRecorder = Hub.AudioRecorder;
        var recorderState = audioRecorder.State.Value;
        if (recorderState is { IsRecording: true, IsVoiceActive: true } && recorderState.ChatId == chatId) {
            await audioRecorder.State.Computed
                .When(s => !s.IsVoiceActive || !s.IsRecording || s.ChatId != chatId, cancellationToken)
                .ConfigureAwait(false);
        }

        var chatLanguage = await LanguageUI.GetChatLanguageAndPrimary(chatId, cancellationToken).ConfigureAwait(false);
        if (chatLanguage.Item1 == language)
            return; // language already set explicitly
        await LanguageUI.ChangeChatLanguage(chatId, language, cancellationToken).ConfigureAwait(false);
        _ = Dispatcher.InvokeAsync(() => {
            ToastUI.Show(
                L.Transcription_LanguageDetected_Format(chat.Title, language.Title),
                () => _ = ModalUI.Show(new VoiceSettingsModal.Model(chatId), CancellationToken.None),
                L.Common_Change,
                ToastDismissDelay.Long);
        });
    }

    private bool IsRecentDetection(Moment timestamp)
        => (Clocks.SystemClock.Now - timestamp).Positive().TotalSeconds < 60;

    private async Task PrefetchChatTails(CancellationToken cancellationToken)
    {
        // NOTE(AY): We must rewrite this logic as follow:
        // - We don't care about visible chats - we periodically prefetch the tails of all chats
        // - Periodically = prefech next 100 tails every 10 min (w/ 1 min startup delay)
        // - We should iterate through every chat in user's contact list, even though
        //   we may also end up prefetching only top 100 chats by recency in every list
        // - Ultimately, our goal is to make sure nearly every chat is available offline.

        // NOTE(AK): This is a temporary solution to prefetch chat tails
        // - We start prefetching tails of visible chats with delay
        // - We prefetch tails of changed chats only - ChatInfo is updated when new entries are added
        await Clocks.CoarseSystemClock.Delay(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);

        var visibleChatsChanges = ChatListUI.VisibleChats.Computed.Changes(cancellationToken);
        var changeTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await foreach (var cVisibleChats in visibleChatsChanges.ConfigureAwait(false)) {
            if (cVisibleChats.Value.Count == 0)
                continue;

            changeTokenSource.CancelAndDisposeSilently();
            changeTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var changeToken = changeTokenSource.Token;
            var visibleChatIds = cVisibleChats.Value;
            foreach (var chatId in visibleChatIds)
                _ = BackgroundTask.Run(async () => {
                    var cGet = await Computed.Capture(() => Get(chatId, changeToken), changeToken).ConfigureAwait(false);
                    var chatInfoChanges = cGet.Changes(changeToken);
                    await foreach (var cChatInfo in chatInfoChanges.ConfigureAwait(false)) {
                        var chatInfo = cChatInfo.Value;
                        if (chatInfo == null)
                            continue;

                        var lastReadEntryLid = chatInfo.ReadEntryLid;
                        var prefetchNearTo = lastReadEntryLid > 0
                            ? lastReadEntryLid
                            : chatInfo.News?.TextEntryLidRange.End ?? 0;
                        var idTile = IdTileStack.LastLayer.GetTile(prefetchNearTo).Range;
                        var chatDataQuery = new ChatDataQuery(idTile, -HalfLoadLimit, LoadLimit);
                        // isPrefetch: the public overload logs as a real query and spawns a second chain.
                        await GetChatItemsInternal(chatId, chatDataQuery, lastReadEntryLid, true, changeToken)
                            .SilentAwait(false);
                        // ChatInfo moves on every new entry and read-position change; Changes() coalesces.
                        await Clocks.CoarseSystemClock.Delay(PrefetchThrottle, changeToken).ConfigureAwait(false);
                    }
                }, changeToken);
        }
    }
}
