using ActualChat.Chat.Db;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Hashing;
using ActualChat.Queues;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class ChatEntryLanguagesBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), IChatEntryLanguagesBackend
{
    private static readonly TileLayer<long> EntryIdTiles = Constants.Chat.EntryIdTiles;

    private IDbEntityResolver<string, DbChatEntryLanguage> EntityResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbChatEntryLanguage>>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private LanguageDetector LanguageDetector => field ??= Services.GetRequiredService<LanguageDetector>();
    private IQueues Queues => field ??= Services.Queues();
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();

    // [ComputeMethod]
    public virtual async Task<ChatLanguageTile> GetTile(
        ChatId chatId,
        Range<long> lidTileRange,
        CancellationToken cancellationToken)
    {
        var idTile = EntryIdTiles.GetTile(lidTileRange);
        var entries = await GetEntryIds(idTile.Range)
            .Select(id => Get(id, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return new ChatLanguageTile(idTile.Range, entries.SkipNullItems().ToArray());

        IEnumerable<ChatEntryId> GetEntryIds(Range<long> range)
        {
            for (var lid = range.Start; lid < range.End; lid++)
                yield return ChatEntryId.New(chatId, lid);
        }
    }

    // [CommandHandler]
    public virtual async Task<ChatEntryLanguage?> OnDetect(
        ChatEntryLanguagesBackend_Detect command,
        CancellationToken cancellationToken)
    {
        var id = command.Id;
        if (Invalidation.IsActive)
            return default!; // It just spawns other commands, so nothing to do here

        var (entry, entryLanguage) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!entry.NeedsLanguageDetection(entryLanguage))
            return entryLanguage;

        var languages = await LanguageDetector.DetectLanguages(entry.Content, cancellationToken).ConfigureAwait(false);
        entryLanguage ??= new ChatEntryLanguage(id);
        entryLanguage = entryLanguage with {
            Languages = [..languages],
            EntryContentHash = entry.ContentHash,
        };

        var cmd = new ChatEntryLanguagesBackend_Change(id, entryLanguage.Version, Change.Upsert(entryLanguage));
        return await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<ChatEntryLanguage?> OnChange(
        ChatEntryLanguagesBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        id.Require();
        change.RequireValid();
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            if (context.Operation.Items.KeylessGet<bool>())
                _ = Get(id, default);
            return default!; // only bulk changes trigger invalidation
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        await dbContext.ChatEntryLanguages.LockShared(id, cancellationToken).ConfigureAwait(false);
        var dbChatEntryLanguage = await dbContext.ChatEntryLanguages
            .FirstOrDefaultAsync(c => c.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbChatEntryLanguage?.ToModel();
        var now = Clocks.SystemClock.Now;

        if (change.IsCreate(out var chatEntryLanguage)) {
            if (existing != null)
                return existing;

            await dbContext.ChatEntryLanguages.Lock(id, cancellationToken).ConfigureAwait(false);
            chatEntryLanguage = chatEntryLanguage with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = now,
                ModifiedAt = now,
            };
            dbChatEntryLanguage = new DbChatEntryLanguage(chatEntryLanguage);
            dbContext.Add(dbChatEntryLanguage);
        }
        else if (change.IsUpdate(out chatEntryLanguage)) {
            dbChatEntryLanguage.RequireVersion(expectedVersion);
            await dbContext.ChatEntryLanguages.Lock(id, cancellationToken).ConfigureAwait(false);
            chatEntryLanguage = chatEntryLanguage with {
                Version = VersionGenerator.NextVersion(dbChatEntryLanguage.Version),
                ModifiedAt = now,
            };
            dbChatEntryLanguage.UpdateFrom(chatEntryLanguage);
            dbContext.ChatEntryLanguages.Update(dbChatEntryLanguage);
        }
        else {
            if (dbChatEntryLanguage == null)
                return null;

            dbChatEntryLanguage.RequireVersion(expectedVersion);
            await dbContext.ChatEntryLanguages.Lock(id, cancellationToken).ConfigureAwait(false);
            dbContext.Remove(dbChatEntryLanguage);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(true);
        return dbChatEntryLanguage.ToModel();
    }

    // [EventHandler]
    public virtual Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            if (context.Operation.Items.KeylessGet<bool>())
                _ = Get(eventCommand.Entry.Id, default);
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here
        }

        if (!Settings.IsTranslationEnabled)
            return Task.CompletedTask;

        var (entry, _, changeKind, oldEntry) = eventCommand;
        return ChangeEntryLanguages();

        async Task ChangeEntryLanguages()
        {
            if (!entry.SupportsLanguageDetection())
                return;

            if (changeKind is ChangeKind.Update && entry.ContentHash == oldEntry?.ContentHash)
                return;

            if (changeKind is ChangeKind.Remove) {
                Log.LogDebug("OnChatEntryChangedEvent: Removing chat entry languages for {Id}", entry.Id);
                await Commander.Call(ChatEntryLanguagesBackend_Change.Remove(entry.Id), true, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var _1 = dbContext.ConfigureAwait(false);

            context.Operation.Items.KeylessSet(true);
        }
    }

    // Protected
    [ComputeMethod]
    protected virtual async Task<ChatEntryLanguage?> Get(ChatEntryId id, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return null;

        var (entry, entryLanguage) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!entry.NeedsLanguageDetection(entryLanguage))
            return entryLanguage ?? new ChatEntryLanguage(id) {
                Languages = [],
                EntryContentHash = entry?.ContentHash ?? HashString.None,
                CreatedAt = Clocks.CoarseSystemClock.Now,
                ModifiedAt = Clocks.CoarseSystemClock.Now,
            }; // Return a new empty language entry if no need to detect languages

        // It's a compute method, we don't want to do any heavy lifting here, so...
        var cmd = new ChatEntryLanguagesBackend_Detect(id, entry.ContentHash);
        await Queues.Enqueue(cmd, cancellationToken).ConfigureAwait(false);
        return entryLanguage;
    }

    // Helper methods

    private async Task<(ChatEntry? Entry, ChatEntryLanguage? Language)> GetExisting(ChatEntryId id, CancellationToken cancellationToken)
    {
        var entry = await ChatsBackend.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return (null, null);

        var dbEntryLanguage = await EntityResolver.Get(id.Value, cancellationToken).ConfigureAwait(false);
        var entryLanguage = dbEntryLanguage?.ToModel();
        return (entry, entryLanguage);
    }
}
