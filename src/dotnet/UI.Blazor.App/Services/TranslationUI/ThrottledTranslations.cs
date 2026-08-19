using ActualChat.Concurrency;

namespace ActualChat.UI.Blazor.App.Services;

public class ThrottledTranslations : UIWorkerBase<AppUIHub>, IComputeService, IAsyncDisposable
{
    public const int ConcurrencyLevel = 10;
    // Bounds a WhenNotNull wait the server may never end, so a slot can't be held for the session
    private static readonly TimeSpan ProcessCallTimeout = TimeSpan.FromSeconds(60);
    private readonly ConcurrentProcessor<TranslationId, Translation> _translations;
    private readonly ConcurrentProcessor<ChatEntryId, ChatEntryLanguage> _languageDetections;

    private ITranslations Translations => Hub.Translations;
    private ChatUI ChatUI => Hub.ChatUI;
    private TranslationUI TranslationUI => Hub.TranslationUI;

    public ThrottledTranslations(AppUIHub hub) : base(hub)
    {
        _translations = new(
            ConcurrencyLevel, WhenTranslated, ProcessCallTimeout,
            log: hub.LogFor<ConcurrentProcessor<TranslationId, Translation>>());
        _languageDetections = new(
            ConcurrencyLevel, WhenLanguageDetected, ProcessCallTimeout,
            log: hub.LogFor<ConcurrentProcessor<ChatEntryId, ChatEntryLanguage>>());
    }

    public async ValueTask DisposeAsync()
    {
        var timeout = CoreConstants.DisposeTimeout;
        try {
            await _translations.DisposeSilentlyAsync().AsTask().WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning(
                "{Type}: _translations didn't dispose in {Timeout}, proceeding",
                GetType().GetName(), timeout);
        }
        try {
            await _languageDetections.DisposeSilentlyAsync().AsTask().WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning(
                "{Type}: _languageDetections didn't dispose in {Timeout}, proceeding",
                GetType().GetName(), timeout);
        }
    }

    public Task<Translation?> GetExisting(TranslationId id, CancellationToken cancellationToken)
        => Translations.Get(Session, id, false, cancellationToken);

    public async Task<Translation?> Get(TranslationId id, CancellationToken cancellationToken)
    {
        var session = Session;
        var existingTranslation = await Translations.Get(session, id, false, cancellationToken).ConfigureAwait(false);
        if (existingTranslation is null)
            _translations.Enqueue(id);

        return existingTranslation;
    }

    public async Task<ChatEntryLanguage?> GetLanguage(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        var existing = await GetLanguageInternal(entryId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            _languageDetections.Enqueue(entryId);

        return existing;
    }

    // Protected/internal methods

#pragma warning disable RCS1210
    // These three are internal to be accessible from tests
    internal Task<Translation>? GetWorkItem(TranslationId id)
        => _translations.Get(id)?.ResultTask;
#pragma warning restore RCS1210

    internal IReadOnlyList<(TranslationId Id, Task<Translation> Task)> ListRunning()
        => _translations.Queue.Where(x => x.IsStarted).Select(x => (x.Key, x.ResultTask)).ToList();

    internal IReadOnlyList<(TranslationId Id, Task<Translation> Task)> ListQueued()
        => _translations.Queue.Where(x => !x.IsStarted).Select(x => (x.Key, x.ResultTask)).ToList();

    [ComputeMethod]
    protected virtual async Task<State?> GetTranslationVisibilityState(CancellationToken cancellationToken)
    {
        var itemVisibility = await ChatUI.ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
        if (itemVisibility.IsEmpty
            || await TranslationUI.IsEnabled(itemVisibility.ChatId, cancellationToken).ConfigureAwait(false) != true)
            return null;

        var targetLanguage = await TranslationUI
            .GetTranslationLanguage(itemVisibility.ChatId, cancellationToken)
            .ConfigureAwait(false);

        return new(itemVisibility, targetLanguage);
    }

    [ComputeMethod]
    protected virtual Task<ChatEntryLanguage?> GetLanguageInternal(
        ChatEntryId entryId, CancellationToken cancellationToken)
        => Translations.GetLanguage(Session, entryId, cancellationToken);

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(SyncRunningTranslations),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return baseChains
            .Select(chain => chain.Log(LogLevel.Debug, Log).RetryForever(retryDelays, Log))
            .RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task SyncRunningTranslations(CancellationToken cancellationToken)
    {
        var cState0 = await Computed
            .Capture(() => GetTranslationVisibilityState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        State? last = null;
        await foreach (var (state, _) in cState0.Changes(cancellationToken).ConfigureAwait(false)) {
            var keys = GetDisappearedKeys(state).ToList();
            var threadEntryIds = await ListThreadPreviewEntryIds(keys).ConfigureAwait(false);
            _translations.RemoveMany(false, GetDisappearedTranslationIds(keys, threadEntryIds).ToArray());
            _languageDetections.RemoveMany(false, GetDisappearedEntryIds(state).Concat(threadEntryIds).ToArray());
            last = state;
        }
        return;

        IEnumerable<ChatMessageKey> GetDisappearedKeys(State? state) {
            if (last is null)
                return [];

            return state is not null
                ? last.ItemVisibility.VisibleKeys.Except(state.ItemVisibility.VisibleKeys)
                : last.ItemVisibility.VisibleKeys;
        }

        IEnumerable<TranslationId> GetDisappearedTranslationIds(
            IReadOnlyList<ChatMessageKey> keys,
            IReadOnlyList<ChatEntryId> threadEntryIds) {
            if (last is null)
                return [];

            var targetLanguage = last.TargetLanguage;
            return ToPossibleTranslationIds(keys, last.ItemVisibility.ChatId, targetLanguage)
                .Concat(threadEntryIds.Select(x => TranslationId.New(x, targetLanguage)));
        }

        IEnumerable<ChatEntryId> GetDisappearedEntryIds(State? state) {
            if (last is null)
                return [];

            return state is not null
                ? last.ItemVisibility.VisibleTextEntryIds.Except(state.ItemVisibility.VisibleTextEntryIds)
                : last.ItemVisibility.VisibleTextEntryIds;
        }

        // A thread card previews entries of the thread chat, so no visible key of the chat we're
        // watching maps to them - they have to be resolved the same way the card resolves them.
        async Task<List<ChatEntryId>> ListThreadPreviewEntryIds(IReadOnlyList<ChatMessageKey> keys) {
            var result = new List<ChatEntryId>();
            if (last is null)
                return result;

            var chatId = last.ItemVisibility.ChatId;
            foreach (var key in keys) {
                if (key.Kind != ChatMessageKind.Thread)
                    continue;

                var threadChatId = chatId.CreateThreadId(key.LocalId);
                var entries = await ChatUI
                    .GetThreadPreviewEntries(threadChatId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                result.AddRange(entries.Select(x => x.Id));
            }

            return result;
        }
    }

    private Task<Translation> WhenTranslated(TranslationId translationId, CancellationToken cancellationToken)
        => WhenNotNull(() => Translations.Get(Session, translationId, true, cancellationToken), cancellationToken);

    private Task<ChatEntryLanguage> WhenLanguageDetected(ChatEntryId id, CancellationToken cancellationToken)
        => WhenNotNull(() => GetLanguageInternal(id, cancellationToken), cancellationToken);

    private static async Task<T> WhenNotNull<T>(Func<Task<T?>> taskFactory, CancellationToken cancellationToken)
    {
        var cData0 = await Computed
            .Capture(taskFactory, cancellationToken)
            .ConfigureAwait(false);
        var cData = await cData0.When(x => x is not null, cancellationToken).ConfigureAwait(false);

        return cData.Value!;
    }

    private static IEnumerable<TranslationId> ToPossibleTranslationIds(
        IEnumerable<ChatMessageKey> keys,
        ChatId chatId,
        Language targetLanguage)
        => keys.SelectMany(x => ToPossibleTranslationIds(x, chatId, targetLanguage));

    private static IEnumerable<TranslationId> ToPossibleTranslationIds(
        ChatMessageKey key,
        ChatId chatId,
        Language targetLanguage)
        => key.Kind switch {
            ChatMessageKind.None => [TranslationId.New(ChatEntryId.New(chatId, key.LocalId), targetLanguage)],
            ChatMessageKind.ConversationStart => [
                TranslationId.New(TranslationSourceId.New(chatId, TranslationIdKind.ConversationTitle, key.LocalId),
                    targetLanguage),
                TranslationId.New(TranslationSourceId.New(chatId, TranslationIdKind.ConversationSummary, key.LocalId),
                    targetLanguage),
                TranslationId.New(
                    TranslationSourceId.New(chatId, TranslationIdKind.ConversationDescription, key.LocalId),
                    targetLanguage),
            ],
            ChatMessageKind.Thread => [
                TranslationId.New(TranslationSourceId.New(chatId, TranslationIdKind.ThreadTitle, key.LocalId),
                    targetLanguage),
                TranslationId.New(TranslationSourceId.New(chatId, TranslationIdKind.ThreadDescription, key.LocalId),
                    targetLanguage),
            ],
            _ => [],
        };

    // Nested types

    protected sealed record State(ChatViewItemVisibility ItemVisibility, Language TargetLanguage);
}
