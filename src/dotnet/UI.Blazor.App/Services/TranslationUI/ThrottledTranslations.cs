using ActualChat.Concurrency;

namespace ActualChat.UI.Blazor.App.Services;

public class ThrottledTranslations : UIWorkerBase<AppUIHub>, IComputeService, IAsyncDisposable
{
    public const int ConcurrencyLevel = 10;
    private readonly ConcurrentProcessor<TranslationId, Translation> _translations;
    private readonly ConcurrentProcessor<ChatEntryId, ChatEntryLanguage> _languageDetections;

    private ITranslations Translations => Hub.Translations;
    private ChatUI ChatUI => Hub.ChatUI;
    private TranslationUI TranslationUI => Hub.TranslationUI;

    public ThrottledTranslations(AppUIHub hub) : base(hub)
    {
        _translations = new (ConcurrencyLevel, WhenTranslated, log: hub.LogFor<ConcurrentProcessor<TranslationId, Translation>>());
        _languageDetections = new (ConcurrencyLevel, WhenLanguageDetected, log: hub.LogFor<ConcurrentProcessor<ChatEntryId, ChatEntryLanguage>>());
    }

    public async ValueTask DisposeAsync()
    {
        var typeName = GetType().GetName();
        await _translations.DisposeSilentlyAsync($"{typeName}._translations", Log).ConfigureAwait(false);
        await _languageDetections.DisposeSilentlyAsync($"{typeName}._languageDetections", Log).ConfigureAwait(false);
    }

    public Task<Translation?> GetExisting(TranslationId id, CancellationToken cancellationToken)
        => Translations.Get(Session, id, false, cancellationToken);

    public async Task<Translation?> Get(
        TranslationId id,
        CancellationToken cancellationToken)
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

    // Internal methods (used in tests)

#pragma warning disable RCS1210
    internal Task<Translation>? GetWorkItem(TranslationId id)
        => _translations.Get(id)?.ResultTask;
#pragma warning restore RCS1210

    internal IReadOnlyList<(TranslationId Id, Task<Translation> Task)> ListRunning()
        => _translations.Queue.Where(x => x.IsStarted).Select(x => (x.Key, x.ResultTask)).ToList();

    internal IReadOnlyList<(TranslationId Id, Task<Translation> Task)> ListQueued()
        => _translations.Queue.Where(x => !x.IsStarted).Select(x => (x.Key, x.ResultTask)).ToList();

    // Protected methods

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
    protected virtual Task<ChatEntryLanguage?> GetLanguageInternal(ChatEntryId entryId, CancellationToken cancellationToken)
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

    private async Task SyncRunningTranslations(CancellationToken cancellationToken)
    {
        var cState0 = await Computed
            .Capture(() => GetTranslationVisibilityState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        State? last = null;
        await foreach (var (state, _) in cState0.Changes(cancellationToken).ConfigureAwait(false)) {
            // TODO(FC): dequeue for thread entries - use ChatUI.GetThreadPreviewEntries
            _translations.RemoveMany(false, GetDisappearedTranslationIds(state).ToArray());
            _languageDetections.RemoveMany(false, GetDisappearedEntryIds(state).ToArray());
            last = state;
        }
        return;

        IEnumerable<TranslationId> GetDisappearedTranslationIds(State? state)
        {
            if (last is null)
                return [];

            var keys = state is not null
                ? last.ItemVisibility.VisibleKeys.Except(state.ItemVisibility.VisibleKeys)
                : last.ItemVisibility.VisibleKeys;
            return ToPossibleTranslationIds(keys, last.ItemVisibility.ChatId, last.TargetLanguage);
        }

        IEnumerable<ChatEntryId> GetDisappearedEntryIds(State? state)
        {
            if (last is null)
                return [];

            return state is not null
                ? last.ItemVisibility.VisibleTextEntryIds.Except(state.ItemVisibility.VisibleTextEntryIds)
                : last.ItemVisibility.VisibleTextEntryIds;
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

    private static IEnumerable<TranslationId> ToPossibleTranslationIds(IEnumerable<ChatMessageKey> keys, ChatId chatId, Language targetLanguage)
        => keys.SelectMany(x => ToPossibleTranslationIds(x, chatId, targetLanguage));

    private static IEnumerable<TranslationId> ToPossibleTranslationIds(ChatMessageKey key, ChatId chatId, Language targetLanguage)
        => key.Kind switch {
            ChatMessageKind.None => [TranslationId.New(ChatEntryId.New(chatId, key.LocalId), targetLanguage)],
            ChatMessageKind.ConversationStart => [
                TranslationId.New(TranslationSourceId.New(chatId, TranslationIdKind.ConversationTitle, key.LocalId),
                    targetLanguage),
                TranslationId.New(TranslationSourceId.New(chatId, TranslationIdKind.ConversationSummary, key.LocalId),
                    targetLanguage),
                TranslationId.New(TranslationSourceId.New(chatId, TranslationIdKind.ConversationDescription, key.LocalId),
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
