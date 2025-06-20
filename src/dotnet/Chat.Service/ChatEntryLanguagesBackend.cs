using ActualChat.Chat.Db;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Queues;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class ChatEntryLanguagesBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), IChatEntryLanguagesBackend
{
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbChatEntryLanguage> EntityResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbChatEntryLanguage>>();
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    [field: AllowNull, MaybeNull]
    private LanguageDetector LanguageDetector => field ??= Services.GetRequiredService<LanguageDetector>();
    [field: AllowNull, MaybeNull]
    private IQueues Queues => field ??= Services.Queues();
    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();

    // [ComputeMethod]
    public virtual async Task<ChatEntryLanguage?> Get(ChatEntryId id, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return null;

        var (entry, entryLanguage) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!NeedsDetection(entry, entryLanguage))
            return entryLanguage;

        // It's a compute method, we don't want to do any heavy lifting here, so...
        var cmd = new ChatEntryLanguagesBackend_Detect(id, entry.ContentHash);
        await Queues.Enqueue(cmd, cancellationToken).ConfigureAwait(false);
        return entryLanguage;
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
        if (!NeedsDetection(entry, entryLanguage))
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
            await dbContext.ChatEntryLanguages.Lock(id, cancellationToken).ConfigureAwait(false);
            dbChatEntryLanguage.RequireVersion(expectedVersion);
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

            await dbContext.ChatEntryLanguages.Lock(id, cancellationToken).ConfigureAwait(false);
            dbChatEntryLanguage.RequireVersion(expectedVersion);
            dbContext.Remove(dbChatEntryLanguage);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(true);
        return dbChatEntryLanguage.ToModel();
    }

    // [EventHandler]
    public virtual Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
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
            if (!SupportsDetection(entry))
                return;

            if (changeKind is ChangeKind.Update && entry.ContentHash == oldEntry?.ContentHash)
                return;

            if (changeKind is ChangeKind.Remove) {
                Log.LogDebug("OnTextEntryChangedEvent: Removing chat entry languages for {Id}", entry.Id);
                await Commander.Call(ChatEntryLanguagesBackend_Change.Remove(entry.Id), true, cancellationToken)
                    .ConfigureAwait(false);
            }

            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var _1 = dbContext.ConfigureAwait(false);

            context.Operation.Items.KeylessSet(true);
        }
    }

    // Helper methods

    private static bool SupportsDetection(ChatEntry entry)
    {
        if (entry.Kind is not ChatEntryKind.Text)
            return false;

        if (entry.IsSystemEntry)
            return false;

        // languages are already saved for transcribed messages
        return entry is { HasAudioEntry: false, HasVideoEntry: false };
    }

    private static bool NeedsDetection([NotNullWhen(true)] ChatEntry? entry, ChatEntryLanguage? entryLanguage)
    {
        if (entry is null)
            return false;

        if (!SupportsDetection(entry))
            return false;

        if (entry.IsRemoved)
            return false;

        if (entryLanguage is null)
            return true;

        return entryLanguage.EntryContentHash != entry.ContentHash;
    }

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
