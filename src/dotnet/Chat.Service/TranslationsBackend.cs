using ActualChat.Chat.Db;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Queues;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;

namespace ActualChat.Chat;

public class TranslationsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), ITranslationsBackend
{
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbTranslation> EntityResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbTranslation>>();
    [field: AllowNull, MaybeNull]
    private Translator Translator => field ??= Services.GetRequiredService<Translator>();
    [field: AllowNull, MaybeNull]
    private Translator RealtimeTranslator => field ??= Services.GetRequiredKeyedService<Translator>(Constants.Translation.RealtimeServiceKey);
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    [field: AllowNull, MaybeNull]
    private IQueues Queues => field ??= Services.Queues();
    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    [field: AllowNull, MaybeNull]
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();

    // [ComputeMethod]
    public virtual async Task<Translation?> Get(TranslationId id, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return null;

        var (translationSource, translation) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!translationSource.NeedsTranslation(translation))
            return translation;

        // we only try to enqueue and fast return to allow compute method to cache current result
        var cmd = new TranslationsBackend_Translate(id, false);
        await Queues.Enqueue(cmd, cancellationToken).ConfigureAwait(false);
        return translation;
    }

    // Not a compute method
    public Task<string> Translate(TranslationId id, string prefix, string content, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return Task.FromResult("");

        if (content.IsNullOrEmpty())
            return Task.FromResult("");

        if (id.Kind is not TranslationIdKind.TextEntry)
            return Task.FromResult("");

        return TranslateInternal(id, prefix, content, true, cancellationToken);
    }

    [ComputeMethod]
    protected virtual async Task<string> GetTranslationContext(ChatEntryId id, int count, CancellationToken cancellationToken)
    {
        var entries = await ListEntries()
            .Take(count)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return string.Join(".\n", entries.Select(e => e.Content));

        async IAsyncEnumerable<ChatEntry> ListEntries()
        {
            for (var maxLidExclusive = id.LocalId; maxLidExclusive >= 0; maxLidExclusive -= Settings.Translation.ContextMessageCount) {
                var minLid = (maxLidExclusive - Settings.Translation.ContextMessageCount).Clamp(0, long.MaxValue);
                var idRange = new Range<long>(minLid, maxLidExclusive);
                var foundEntries = await ChatsBackend.GetEntries(id.ChatId, ChatEntryKind.Text, idRange, false, cancellationToken).ConfigureAwait(false);
                foreach (var entry in foundEntries.Where(e => e.LocalId < maxLidExclusive))
                    yield return entry;
            }
        }
    }

    // [CommandHandler]
    public virtual async Task<Translation?> OnChange(TranslationsBackend_Change command, CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        if (Invalidation.IsActive) {
            _ = Get(id, default);
            return null!;
        }

        if (!Settings.IsTranslationEnabled)
            return null;

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Translations.LockShared(id, cancellationToken).ConfigureAwait(false);
        var dbTranslation = await dbContext.Translations.GetAsNoTracking(id.Value, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;

        if (change.IsCreate(out var translation)) {
            if (dbTranslation != null)
                return dbTranslation.ToModel();

            await dbContext.Translations.Lock(id, cancellationToken).ConfigureAwait(false);
            dbTranslation = new DbTranslation(translation) {
                Id = id.Value,
                CreatedAt = now,
                ModifiedAt = now,
                Version = VersionGenerator.NextVersion(),
            };
            dbContext.Add(dbTranslation);
        }
        else if (change.IsUpdate(out translation)) {
            if (dbTranslation is null)
                return null;

            dbTranslation.RequireVersion(expectedVersion);
            dbContext.Translations.Attach(dbTranslation);
            translation = translation with {
                CreatedAt = dbTranslation.CreatedAt,
                ModifiedAt = now,
                Version = VersionGenerator.NextVersion(dbTranslation.Version),
            };
            dbTranslation.UpdateFrom(translation);
        }
        else
            throw StandardError.NotSupported("Translations cannot be removed.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbTranslation.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<Translation?> OnTranslate(TranslationsBackend_Translate command, CancellationToken cancellationToken)
    {
        var (id, ignoreVersion) = command;
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (translationSource, translation) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!translationSource.NeedsTranslation(translation))
            return translation;

        var translatedText = await TranslateInternal(id, "", translationSource.Content, false, cancellationToken).ConfigureAwait(false);
        translation ??= new Translation(id);
        translation = translation with {
            Content = translatedText,
            SourceContentHash = translationSource.ContentHash,
        };

        var version = ignoreVersion
            ? (long?)null
            : translation.Version;
        try {
            var cmd = new TranslationsBackend_Change(id, version, Change.Upsert(translation));
            return await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }
        catch (VersionMismatchException) {
            // Ignore version mismatch if already translated
        }
        return await Get(id, cancellationToken).ConfigureAwait(false);
    }

    // protected methods

    protected virtual async Task<TranslationSource?> TryResolveTranslationSource(TranslationId translationId, CancellationToken cancellationToken)
    {
        var sourceId = translationId.SourceId;
        switch (sourceId.Kind) {
            case TranslationIdKind.TextEntry: {
                var chatEntryId = sourceId.GetChatEntryId();
                var entry = await ChatsBackend.GetEntry(chatEntryId, cancellationToken).ConfigureAwait(false);
                if (entry is null)
                    return null;

                return new TextEntryTranslationSource(entry, sourceId);
            }
            case TranslationIdKind.ConversationTitle or
                TranslationIdKind.ConversationDescription or
                TranslationIdKind.ConversationSummary: {
                var lid = sourceId.GetRefLId();
                var conversationId = ConversationId.New(sourceId.ChatId, lid);
                var conversation = await ConversationsBackend.Get(conversationId, cancellationToken).ConfigureAwait(false);
                if (conversation is null)
                    return null;

                return new ConversationTranslationSource(conversation, sourceId);
            }
            case TranslationIdKind.ThreadTitle or
                TranslationIdKind.ThreadDescription : {
                var lid = sourceId.GetRefLId();
                var threadChatId = sourceId.ChatId.CreateThreadId(lid);
                var threadChat = await ChatsBackend.Get(threadChatId, cancellationToken).ConfigureAwait(false);
                if (threadChat is null)
                    return null;

                return new ThreadTranslationSource(threadChat, sourceId);
            }
            default: throw new ArgumentOutOfRangeException(nameof(translationId.Kind));
        }
    }

    // Private methods

    private async Task<(TranslationSource? source, Translation? translation)> GetExisting(TranslationId id, CancellationToken cancellationToken)
    {
        var source = await TryResolveTranslationSource(id, cancellationToken).ConfigureAwait(false);
        if (source is null)
            return (null, null);

        var dbTranslation = await EntityResolver.Get(id.Value, cancellationToken).ConfigureAwait(false);
        var translation = dbTranslation?.ToModel();
        return (source, translation);
    }

    private async Task<string> TranslateInternal(TranslationId id, string prefix, string content, bool isRealtime, CancellationToken cancellationToken)
    {
        var hasPrefix = !prefix.IsNullOrEmpty();
        string context = "";
        if (id.Kind is TranslationIdKind.TextEntry) {
            var chatEntryId = id.SourceId.GetChatEntryId();
            var count = hasPrefix
                ? Settings.Translation.ContextMessageCount
                : Settings.Translation.RealtimeContextMessageCount;
            context = await GetTranslationContext(chatEntryId, count, cancellationToken).ConfigureAwait(false);
            if (hasPrefix)
                context = $"{context}\n{prefix}";
        }
        var translator = isRealtime
            ? RealtimeTranslator
            : Translator;
        return await translator.Translate(
            content,
            id.Language,
            context,
            cancellationToken).ConfigureAwait(false);
    }
}
