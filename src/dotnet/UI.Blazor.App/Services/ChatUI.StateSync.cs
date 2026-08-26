using ActualChat.Kvas;
using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatUI
{
    private static readonly TimeSpan PrefetchStartDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PrefetchScanPeriod = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PrefetchBatchPause = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PrefetchSavePeriod = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PrefetchBoundaryEpsilon = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PrefetchMaxAge = TimeSpan.FromDays(365);
    private const int PrefetchBatchSize = 10;
    private const int PrefetchEntryLimit = 100;

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
        // The point of this is that a chat's tail is there when the app is offline, so nothing here looks
        // at what's on screen or at where the chat was read - a read position moves in another browser
        // too, and the tail still has to be here. The prefetcher tracks its own progress instead: per
        // chat, the entry lid it reached and the recency it reached it at.
        await Clocks.CoarseSystemClock.Delay(PrefetchStartDelay, cancellationToken).ConfigureAwait(false);

        var accessor = LocalSettings.AccessorFor<ChatTailPrefetchState>();
        var stored = await accessor.Get(cancellationToken).ConfigureAwait(false);
        var boundary = stored.Boundary;
        var prefetched = new Dictionary<ChatId, ChatTailPrefetch>();
        foreach (var item in stored.Chats)
            prefetched[item.ChatId] = item;
        var savedAt = CpuTimestamp.Now - PrefetchSavePeriod;
        var hasUnsavedChanges = false;
        while (true) {
            // ListAllUnordered covers place chats too - contacts store places rather than the chats in
            // them, and it's the one thing that already walks both.
            var chatById = await ChatListUI.ListAllUnordered(cancellationToken).ConfigureAwait(false);
            // A chat untouched for a year isn't worth the traffic, and on a fresh profile this floor is
            // what keeps the first scan from pulling the tail of every chat ever contacted. It's a floor
            // rather than a stored boundary on purpose: a device clock that's briefly wrong then costs
            // one session's scans instead of poisoning a boundary that only ever moves forward.
            var minTouchedAt = Clocks.SystemClock.Now - PrefetchMaxAge;
            var scanBoundary = boundary > minTouchedAt ? boundary : minTouchedAt;
            var pending = new List<ChatInfo>();
            var maxTouchedAt = Moment.EpochStart;
            foreach (var chat in chatById.Values) {
                // Two comparisons and nothing else: this runs over every contact, and there can be
                // thousands of them.
                var touchedAt = chat.Contact.TouchedAt;
                if (touchedAt > maxTouchedAt)
                    maxTouchedAt = touchedAt;
                if (touchedAt <= scanBoundary)
                    continue;
                if (prefetched.TryGetValue(chat.Id, out var done) && done.TouchedAt >= touchedAt)
                    continue;

                pending.Add(chat);
            }

            var hasMore = pending.Count > PrefetchBatchSize;
            if (hasMore) {
                pending.Sort(static (a, b) => b.Contact.TouchedAt.CompareTo(a.Contact.TouchedAt));
                pending.RemoveRange(PrefetchBatchSize, pending.Count - PrefetchBatchSize);
            }
            var hasFailures = false;
            foreach (var chat in pending) {
                var entryLid = prefetched.GetValueOrDefault(chat.Id)?.EntryLid ?? 0;
                try {
                    entryLid = await PrefetchChatTail(chat, entryLid, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    // Left unrecorded, and the boundary held back below, so the next scan tries it again
                    DebugLog?.LogDebug(e, "PrefetchChatTails: failed for #{ChatId}", chat.Id);
                    hasFailures = true;
                    continue;
                }
                prefetched[chat.Id] = new ChatTailPrefetch(chat.Id, chat.Contact.TouchedAt, entryLid);
                hasUnsavedChanges = true;
            }

            var newBoundary = maxTouchedAt - PrefetchBoundaryEpsilon;
            if (!hasMore && !hasFailures && newBoundary > boundary) {
                // Everything above the boundary is done, so it moves up to the newest chat we saw -
                // TouchedAt only ever goes forward, and it's server-stamped, unlike our own clock. The
                // epsilon leaves room for an update that lands with a slightly earlier TouchedAt than one
                // already seen; what stays above the boundary stays in the map, which is what keeps it
                // from being prefetched twice.
                boundary = newBoundary;
                var below = prefetched.Where(kv => kv.Value.TouchedAt <= boundary).Select(kv => kv.Key).ToList();
                foreach (var chatId in below)
                    prefetched.Remove(chatId);
                hasUnsavedChanges = true;
            }

            if (hasUnsavedChanges && savedAt.Elapsed >= PrefetchSavePeriod) {
                var state = new ChatTailPrefetchState {
                    Boundary = boundary,
                    Chats = new ApiArray<ChatTailPrefetch>(prefetched.Values.ToArray()),
                };
                await accessor.Set(state, cancellationToken).ConfigureAwait(false);
                savedAt = CpuTimestamp.Now;
                hasUnsavedChanges = false;
            }

            await Clocks.CoarseSystemClock
                .Delay(hasMore ? PrefetchBatchPause : PrefetchScanPeriod, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<long> PrefetchChatTail(
        ChatInfo chat,
        long prefetchedEntryLid,
        CancellationToken cancellationToken)
    {
        var lidRange = chat.News?.TextEntryLidRange ?? default;
        var end = lidRange.End;
        if (end <= 0)
            return prefetchedEntryLid;

        // Never more than the last few pages: a chat seen for the first time, or one that moved while we
        // were offline, has to cost about as much as one that got a single message.
        var start = Math.Max(lidRange.Start, Math.Max(prefetchedEntryLid, end - PrefetchEntryLimit));
        if (start >= end)
            return end;

        var idTiles = IdTileStack.LastLayer
            .GetCoveringTiles(new Range<long>(start, end))
            .Select(t => t.Range)
            .ToList();
        await PrefetchLoadZone(chat.Id, idTiles, chat.Chat.IsSummarized ?? false, cancellationToken)
            .ConfigureAwait(false);
        return end;
    }
}
