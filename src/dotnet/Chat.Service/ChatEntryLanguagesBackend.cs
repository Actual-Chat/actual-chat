using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Chat.Module;
using ActualChat.Flows;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class ChatEntryLanguagesBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IChatEntryLanguagesBackend
{
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbChatEntryLanguage> EntryLanguageEntityResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbChatEntryLanguage>>();
    [field: AllowNull, MaybeNull]
    private IFlows Flows => field ??= Services.GetRequiredService<IFlows>();
    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();

    // [ComputeMethod]
    public virtual async Task<ChatEntryLanguage?> GetLanguage(ChatEntryId id, CancellationToken cancellationToken)
    {
        var dbEntryLanguage = await EntryLanguageEntityResolver.Get(id, cancellationToken).ConfigureAwait(false);
        return dbEntryLanguage?.ToModel();
    }

    // Not a [ComputeMethod]!
    public async Task<ApiArray<ChatEntryLanguage>> ListForDetection(int limit, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbEntries = await dbContext.ChatEntryLanguages
            .Where(x => x.Languages == "")
            .OrderBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbEntries.Select(x => x.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<ApiArray<Result<ChatEntryLanguage?>>> OnBulkChange(
        ChatEntryLanguagesBackend_BulkChange command,
        CancellationToken cancellationToken)
    {
        var changes = command.Changes;
        if (Invalidation.IsActive) {
            foreach (var change in changes)
                _ = GetLanguage(change.Id, default);
            return default!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var results = new List<Result<ChatEntryLanguage?>>();
        foreach (var change in changes)
            try {
                var result = await ChangeItem(dbContext, change, cancellationToken).ConfigureAwait(false);
                results.Add(result);
            }
            catch (Exception e) {
                Log.LogError(e,
                    "Failed to {ChangeKind} chat entry language #{Id}",
                    change.Change.Kind.ToString().ToLowerInvariant(),
                    change.Id);
                results.Add(Result.Error<ChatEntryLanguage?>(e));
            }
        return results.ToApiArray();
    }

    // [EventHandler]
    public virtual Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        if (!Settings.IsTranslationEnabled)
            return Task.CompletedTask;

        var (entry, _, changeKind, oldEntry) = eventCommand;
        return ChangeEntryLanguages();

        async Task ChangeEntryLanguages()
        {
            if (entry.Kind is not ChatEntryKind.Text)
                return;

            if (!entry.IsSystemEntry) {
                await EnsureEntryLanguage().ConfigureAwait(false);
                await Flows.GetAndResume<LanguageDetectionFlow>("",
                        Settings.LanguageDetectionDelay,
                        "TextEntryChanged",
                        Settings.LanguageDetectionDelay,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        async Task EnsureEntryLanguage()
        {
            if (changeKind is ChangeKind.Remove)
                return;

            // languages are already saved for transcribed messages
            if (entry is not { HasAudioEntry: false, HasVideoEntry: false })
                return;

            if (changeKind is ChangeKind.Update && entry.ContentHash != oldEntry?.ContentHash)
                return;

            var language = changeKind is ChangeKind.Update
                ? await GetLanguage(entry.Id, cancellationToken).ConfigureAwait(false) ?? new ChatEntryLanguage(entry.Id)
                : new ChatEntryLanguage(entry.Id);
            var cmd = ChatEntryLanguagesBackend_BulkChange.Upserts(language with { Languages = [] });
            await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ChatEntryLanguage?> ChangeItem(ChatDbContext dbContext, ChatEntryLanguageChange itemChange, CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = itemChange;
        id.Require();
        change.RequireValid();

        var dbChatEntryLanguage = await dbContext.ChatEntryLanguages
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        var existing = dbChatEntryLanguage?.ToModel();
        var now = Clocks.SystemClock.Now;

        if (change.IsCreate(out var chatEntryLanguage)) {
            if (existing != null)
                return existing;

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
            dbContext.Remove(dbChatEntryLanguage);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbChatEntryLanguage.ToModel();
    }
}
