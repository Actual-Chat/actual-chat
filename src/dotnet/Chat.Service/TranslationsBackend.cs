using ActualChat.Chat.Db;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Mesh;
using ActualChat.Queues;
using ActualChat.Transcription;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class TranslationsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), ITranslationsBackend
{
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbTranslation> EntityResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbTranslation>>();
    [field: AllowNull, MaybeNull]
    private Translator Translator => field ??= Services.GetRequiredService<Translator>();
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    [field: AllowNull, MaybeNull]
    private IMeshLocks TranslationLocks => field ??= Services.MeshLocks<ChatDbContext>().WithKeyPrefix(nameof(TranslationLocks));
    [field: AllowNull, MaybeNull]
    private IQueues Queues => field ??= Services.Queues();
    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();

    // [ComputeMethod]
    public virtual async Task<Translation?> Get(TranslationId id, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return null;

        var (entry, translation) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!NeedsTranslate(entry, translation))
            return translation;

        // we only try to enqueue and fast return to allow compute method to cache current result
        var cmd = new TranslationsBackend_Translate(id);
        await Queues.Enqueue(cmd, cancellationToken).ConfigureAwait(false);
        return translation;
    }

    // [ComputeMethod]
    public virtual Task<string> GetRealtime(TranslationId id, string content, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return Task.FromResult("");

        if (content.IsNullOrEmpty())
            return Task.FromResult("");

        return TranslateUnsafe(id, content, cancellationToken);
    }

    [ComputeMethod(AutoInvalidationDelay = 60 * 1000)]
    protected virtual async Task<string> GetTranslationContext(ChatEntryId id, CancellationToken cancellationToken)
    {
        var entries = await ListEntries().Take(10).ToListAsync(cancellationToken).ConfigureAwait(false);
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
            return default!;
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
        var id = command.Id;
        if (Invalidation.IsActive)
            return default!; // It just spawns other commands, so nothing to do here

        var (entry, translation) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!NeedsTranslate(entry, translation))
            return translation;

        var (_, updatedTranslation) = await TranslationLocks.TryRunLocked(
                $"{id}.{entry.ContentHash}",
                RunLockedOptions.NoRelock,
                ct => GetOrTranslateUnsafe(id, ct),
                cancellationToken)
            .ConfigureAwait(false);
        if (updatedTranslation is null)
            return null;

        var cmd = new TranslationsBackend_Change(id, updatedTranslation.Version, Change.Upsert(updatedTranslation));
        return await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static bool NeedsTranslate([NotNullWhen(true)] ChatEntry? entry, Translation? translation)
    {
        if (entry is null)
            return false;

        if (entry.IsSystemEntry || entry.Kind != ChatEntryKind.Text || entry.IsRemoved || entry.Content.IsNullOrEmpty())
            return false;

        if (translation is null)
            return true;

        if (translation.IsStreaming)
            return false;

        return translation.SourceContentHash != entry.ContentHash;
    }

    private async Task<(ChatEntry? entry, Translation? translation)> GetExisting(TranslationId id, CancellationToken cancellationToken)
    {
        var entry = await ChatsBackend.GetEntry(id.ChatEntryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return (null, null);

        var dbTranslation = await EntityResolver.Get(id.Value, cancellationToken).ConfigureAwait(false);
        var translation = dbTranslation?.ToModel();
        return (entry, translation);
    }

    private async Task<Translation?> GetOrTranslateUnsafe(TranslationId id, CancellationToken cancellationToken)
    {
        var (entry, translation) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!NeedsTranslate(entry, translation))
            return translation;

        var translatedText = await TranslateUnsafe(id, entry.Content, cancellationToken).ConfigureAwait(false);
        translation ??= new Translation(id);
        return translation with {
            Content = translatedText,
            SourceContentHash = entry.ContentHash,
            TimeMap = entry.TimeMap.Scale(entry.Content.Length, translatedText.Length),
        };
    }

    private async Task<string> TranslateUnsafe(TranslationId id, string content, CancellationToken cancellationToken)
    {
        var context = await GetTranslationContext(id.ChatEntryId, cancellationToken).ConfigureAwait(false);
        return await Translator.Translate(
            content,
            id.Language,
            context,
            cancellationToken).ConfigureAwait(false);
    }
}
