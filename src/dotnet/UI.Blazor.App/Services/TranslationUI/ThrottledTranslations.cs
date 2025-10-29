namespace ActualChat.UI.Blazor.App.Services;

public class ThrottledTranslations : UIWorkerBase<AppUIHub>, IComputeService, IAsyncDisposable
{
    public const int ParallelismDegree = 10;
    private readonly ThrottledWorkQueue<TranslationId, Translation> _translationQueue;
    private readonly ThrottledWorkQueue<TextEntryId, ChatEntryLanguage> _languageQueue;

    private ITranslations Translations => Hub.Translations;

    private ChatUI ChatUI => Hub.ChatUI;
    private TranslationUI TranslationUI => Hub.TranslationUI;

    public ThrottledTranslations(AppUIHub hub) : base(hub)
    {
        _translationQueue = new (ParallelismDegree, WhenTranslated, hub.LogFor<ThrottledWorkQueue<TranslationId, Translation>>());
        _languageQueue = new (ParallelismDegree, WhenLanguageDetected, hub.LogFor<ThrottledWorkQueue<TextEntryId, ChatEntryLanguage>>());
    }

    public async ValueTask DisposeAsync()
    {
        await _translationQueue.DisposeSilentlyAsync().ConfigureAwait(false);
        await _languageQueue.DisposeSilentlyAsync().ConfigureAwait(false);
    }

    public Task<Translation?> GetExisting(TranslationId id, CancellationToken cancellationToken)
        => Translations.Get(Session, id, false, cancellationToken);

    public async Task<Translation?> Get(
        TranslationId id,
        string consumerId,
        CancellationToken cancellationToken)
    {
        var session = Session;
        var existingTranslation = await Translations.Get(session, id, false, cancellationToken).ConfigureAwait(false);
        if (existingTranslation is null)
            await _translationQueue.Enqueue(id, consumerId, cancellationToken).ConfigureAwait(false);
        return existingTranslation;
    }

    public async Task<ChatEntryLanguage?> GetLanguage(TextEntryId entryId, string consumerId, CancellationToken cancellationToken)
    {
        var existing = await GetLanguageInternal(entryId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            await _languageQueue.Enqueue(entryId, consumerId, cancellationToken).ConfigureAwait(false);

        return existing;
    }

    internal Task<Translation>? GetWorkItem(TranslationId id)
        => _translationQueue.Get(id);

    internal IReadOnlyList<(TranslationId Id, Task<Translation> Task)> ListRunning()
        => _translationQueue.ListRunning();

    internal IReadOnlyList<(TranslationId Id, Task<Translation> Task)> ListAll()
        => _translationQueue.ListAll();

    internal IReadOnlyList<(TranslationId Id, Task<Translation> Task)> ListQueued()
        => _translationQueue.ListQueued();

    [ComputeMethod]
    protected virtual async Task<State?> GetTranslationVisibilityState(CancellationToken cancellationToken)
    {
        var itemVisibility = await ChatUI.ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
        if (itemVisibility.IsEmpty
            || await TranslationUI.IsEnabled(itemVisibility.ChatId, cancellationToken).ConfigureAwait(false) != true)
            return null;

        var targetLanguage = await TranslationUI.GetTargetLanguage(itemVisibility.ChatId, cancellationToken).ConfigureAwait(false);
        return new(itemVisibility, targetLanguage);
    }

    [ComputeMethod]
    protected virtual Task<ChatEntryLanguage?> GetLanguageInternal(TextEntryId entryId, CancellationToken cancellationToken)
        => Translations.GetLanguage(Session, entryId, cancellationToken);

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(SyncRunningTranslations),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task SyncRunningTranslations(CancellationToken cancellationToken)
    {
        var cState0 = await Computed
            .Capture(() => GetTranslationVisibilityState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        State? last = null;
        await foreach (var cState in cState0.Changes(cancellationToken).ConfigureAwait(false)) {
            // TODO(FC): dequeue for thread entries - use ChatUI.GetThreadPreviewEntries
            _translationQueue.Dequeue(TranslationConsumers.ChatView, GetDisappearedTranslationIds(cState));
            _languageQueue.Dequeue(TranslationConsumers.ChatView, GetDisappearedEntryIds(cState));
            last = cState.Value;
        }
        return;

        IEnumerable<TranslationId> GetDisappearedTranslationIds(Computed<State?> cState)
        {
            if (last is null)
                return [];

            return cState.Value is { } state
                ? ToPossibleTranslationIds(
                    last.ItemVisibility.VisibleKeys.Except(state.ItemVisibility.VisibleKeys),
                    last.ItemVisibility.ChatId,
                    last.TargetLanguage)
                : ToPossibleTranslationIds(last.ItemVisibility.VisibleKeys,
                    last.ItemVisibility.ChatId,
                    last.TargetLanguage);
        }

        IEnumerable<TextEntryId> GetDisappearedEntryIds(Computed<State?> cState)
        {
            if (last is null)
                return [];

            return cState.Value is { } state
                ? last.ItemVisibility.VisibleTextEntryIds.Except(state.ItemVisibility.VisibleTextEntryIds)
                : last.ItemVisibility.VisibleTextEntryIds;
        }
    }

    private Task<Translation> WhenTranslated(TranslationId translationId, CancellationToken cancellationToken)
        => WhenNotNull(() => Translations.Get(Session, translationId, true, cancellationToken), cancellationToken);

    private Task<ChatEntryLanguage> WhenLanguageDetected(TextEntryId id, CancellationToken cancellationToken)
        => WhenNotNull(() => GetLanguageInternal(id, cancellationToken), cancellationToken);

    private async Task<T> WhenNotNull<T>(Func<Task<T?>> taskFactory, CancellationToken cancellationToken)
    {
        var cData0 = await Computed
            .Capture(taskFactory, cancellationToken)
            .ConfigureAwait(false);
        var cData = await cData0.When(x => x is not null, cancellationToken).ConfigureAwait(false);
        return cData.Value!;
    }

    private IEnumerable<TranslationId> ToPossibleTranslationIds(IEnumerable<ChatMessageKey> keys, ChatId chatId, Language targetLanguage)
        => keys.SelectMany(x => ToPossibleTranslationIds(x, chatId, targetLanguage));

    private IEnumerable<TranslationId> ToPossibleTranslationIds(ChatMessageKey key, ChatId chatId, Language targetLanguage)
        => key.Kind switch {
            ChatMessageKind.None => [TranslationId.New(TextEntryId.New(chatId, key.LocalId), targetLanguage)],
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
