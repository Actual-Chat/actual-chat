using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Mesh;
using ActualChat.Queues;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

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
        var dbTranslation = await dbContext.Translations.GetAsNoTracking(id, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;

        if (change.IsCreate(out var translation)) {
            if (dbTranslation != null)
                return dbTranslation.ToModel();

            await dbContext.Translations.Lock(id, cancellationToken).ConfigureAwait(false);
            dbTranslation = new DbTranslation(translation) {
                Id = id,
                CreatedAt = now,
                ModifiedAt = now,
                Version = VersionGenerator.NextVersion(),
            };
            dbContext.Add(dbTranslation);
        } else if (change.IsUpdate(out translation)) {
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

        var (_, updatedTranslation) = await TranslationLocks.TryRunLocked($"{id}.{entry.ContentHash}", RunLockedOptions.NoRelock, Translate, cancellationToken).ConfigureAwait(false);
        return updatedTranslation;

        async Task<Translation?> Translate(CancellationToken cancellationToken1)
        {
            (entry, translation) = await GetExisting(id, cancellationToken1).ConfigureAwait(false);
            if (!NeedsTranslate(entry, translation))
                return translation;

            var translatedText = await Translator.Translate(entry.Content, id.Language, cancellationToken1)
                .ConfigureAwait(false);
            translation ??= new Translation(id);
            translation = translation with { Content = translatedText, SourceContentHash = entry.ContentHash };
            var cmd = new TranslationsBackend_Change(id, translation.Version, Change.Upsert(translation));
            return await Commander.Call(cmd, cancellationToken1).ConfigureAwait(false);
        }
    }

    // Private methods

    private static bool NeedsTranslate([NotNullWhen(true)] ChatEntry? entry, Translation? translation)
        => entry != null && (translation == null || translation.SourceContentHash != entry.ContentHash);

    private async Task<(ChatEntry? entry, Translation? translation)> GetExisting(TranslationId id, CancellationToken cancellationToken)
    {
        var entry = await ChatsBackend.GetEntry(id.ChatEntryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return (null, null);

        var dbTranslation = await EntityResolver.Get(id, cancellationToken).ConfigureAwait(false);
        var translation = dbTranslation?.ToModel();
        return (entry, translation);
    }
}
