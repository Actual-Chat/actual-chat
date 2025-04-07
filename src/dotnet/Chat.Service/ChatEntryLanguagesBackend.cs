using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Flows;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class ChatEntryLanguagesBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), IChatEntryLanguagesBackend
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
    public async Task<ChatEntryLanguage[]> ListForDetection(int limit, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbEntries = await dbContext.ChatEntryLanguages
            .Where(x => string.IsNullOrEmpty(x.Languages))
            .OrderBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbEntries.Select(x => x.ToModel()).ToArray();
    }

    // [CommandHandler]
    public virtual async Task<Result<ChatEntryLanguage?>[]> OnBulkChange(
        ChatEntryLanguagesBackend_BulkChange command,
        CancellationToken cancellationToken)
    {
        var changes = command.Changes;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invLanguages = context.Operation.Items.KeylessGet<ChatEntryLanguage[]>();
            if (invLanguages != null) {
                foreach (var entryLanguage in invLanguages)
                    _ = GetLanguage(entryLanguage.Id, default);
            }
            return default!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var results = await changes
            .Select<ChatEntryLanguageChange, Task<Result<ChatEntryLanguage?>>>(change
                => Commander.Call(new ChatEntryLanguagesBackend_TryChange(change), true, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var changed = results.Where(x => !x.HasError).Select(x => x.Value!).ToArray();
        Log.LogDebug("Changed languages for {Count} entries: {Ids}", changed.Length, changed.Select(x => x.Id));
        if (changed.Length > 0) {
            context.Operation.Items.KeylessSet(changed);
            context.Operation.AddEvent(new ChatEntryLanguagesChangedEvent(changed));
        }

        return results.ToArray();
    }

    // [CommandHandler]
    public virtual async Task<ChatEntryLanguage?> OnReset(
        ChatEntryLanguagesBackend_Reset command,
        CancellationToken cancellationToken)
    {
        var id = command.Id;
        id.Require();
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            _ = GetLanguage(id, default);
            return default!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        await Lock(dbContext, id, cancellationToken).ConfigureAwait(false);
        var dbEntryLanguage = await dbContext.ChatEntryLanguages
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (dbEntryLanguage == null) {
            dbEntryLanguage = new () {
                Id = id,
                CreatedAt = now,
                ModifiedAt = now,
                Version = VersionGenerator.NextVersion(),
                Languages = "",
            };
            dbContext.Add(dbEntryLanguage);
        }
        else {
            dbEntryLanguage.Languages = "";
            dbEntryLanguage.ModifiedAt = now;
            dbEntryLanguage.Version = VersionGenerator.NextVersion(dbEntryLanguage.Version);
            dbContext.ChatEntryLanguages.Update(dbEntryLanguage);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var entryLanguage = dbEntryLanguage.ToModel();
        context.Operation.AddEvent(new ChatEntryLanguagesChangedEvent([entryLanguage]));
        return entryLanguage;
    }

    // [CommandHandler]
    public virtual async Task<Result<ChatEntryLanguage?>> OnTryChange(
        ChatEntryLanguagesBackend_TryChange command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command.Change;
        id.Require();
        change.RequireValid();

        if (Invalidation.IsActive)
            return default!; // only bulk changes trigger invalidation

        try {
            return await Change().ConfigureAwait(false);
        }
        catch (Exception e) {
            return e.IsCancellationOf(cancellationToken)
                ? Result.New<ChatEntryLanguage?>(null)
                : Result.NewError<ChatEntryLanguage?>(e);
        }

        async Task<Result<ChatEntryLanguage?>> Change()
        {
            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var _1 = dbContext.ConfigureAwait(false);

            await LockShared(dbContext, id, cancellationToken).ConfigureAwait(false);
            var dbChatEntryLanguage = await dbContext.ChatEntryLanguages
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);
            var existing = dbChatEntryLanguage?.ToModel();
            var now = Clocks.SystemClock.Now;

            if (change.IsCreate(out var chatEntryLanguage)) {
                if (existing != null)
                    return Result.New<ChatEntryLanguage?>(existing);

                await Lock(dbContext, id, cancellationToken).ConfigureAwait(false);
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
                await Lock(dbContext, id, cancellationToken).ConfigureAwait(false);
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
                    return Result.New<ChatEntryLanguage?>(null);

                await Lock(dbContext, id, cancellationToken).ConfigureAwait(false);
                dbChatEntryLanguage.RequireVersion(expectedVersion);
                dbContext.Remove(dbChatEntryLanguage);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.New<ChatEntryLanguage?>(dbChatEntryLanguage.ToModel());
        }
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
            if (entry.Kind is not ChatEntryKind.Text || entry.IsSystemEntry)
                return;

            // languages are already saved for transcribed messages
            if (entry is not { HasAudioEntry: false, HasVideoEntry: false })
                return;

            if (changeKind is ChangeKind.Remove)
                return;

            if (changeKind is ChangeKind.Update && entry.ContentHash == oldEntry?.ContentHash)
                return;

            Log.LogDebug("OnTextEntryChangedEvent: Resetting chat entry languages for {Id}", entry.Id);
            var cmd = new ChatEntryLanguagesBackend_Reset(entry.Id);
            await Commander.Call(cmd, true, cancellationToken).Require().ConfigureAwait(false);
            Log.LogDebug("OnTextEntryChangedEvent: Reset chat entry languages for {Id}", entry.Id);
        }
    }

    // [EventHandler]
    public virtual Task OnChatEntryLanguagesChangedEvent(
        ChatEntryLanguagesChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return Task.CompletedTask; // It just spawns other commands, so nothing to do here

        return Flows.GetAndResume<LanguageDetectionFlow>("",
            Settings.LanguageDetectionDelay,
            nameof(OnChatEntryLanguagesChangedEvent),
            Settings.LanguageDetectionDelay,
            cancellationToken);
    }

    // Helper methods

    private static Task LockShared(ChatDbContext dbContext, ChatEntryId id, CancellationToken cancellationToken)
        => dbContext.ChatEntryLanguages.LockShared(GetLockKey(id), cancellationToken);

    private static Task Lock(ChatDbContext dbContext, ChatEntryId id, CancellationToken cancellationToken)
        => dbContext.ChatEntryLanguages.Lock(GetLockKey(id), cancellationToken);

    private static string GetLockKey(ChatEntryId id)
        => "lang_" + id;
}
