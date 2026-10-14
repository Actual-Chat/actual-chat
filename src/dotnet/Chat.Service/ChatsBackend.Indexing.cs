using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    // Not a [ComputeMethod]!
    public async Task<Chat[]> List(
        Moment minCreatedAt,
        ChatId? lastChatId,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dMinCreatedAt = minCreatedAt.ToDateTime(DateTime.MinValue, DateTime.MaxValue);
        var dbChats = await dbContext.Chats
            .Where(x => x.CreatedAt >= dMinCreatedAt)
            .OrderBy(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbChats.Count == 0)
            return [];

        if (lastChatId is null || dbChats[0].CreatedAt > dMinCreatedAt)
            // no chats created at minCreatedAt that we need to skip
            return dbChats.Select(x => x.ToModel()).ToArray();

        var lastChatIdx = dbChats.FindIndex(x => ChatId.Parse(x.Id) == lastChatId);
        if (lastChatIdx < 0)
            return dbChats.Select(x => x.ToModel()).ToArray();

        return dbChats.Skip(lastChatIdx + 1).Select(x => x.ToModel()).ToArray();
    }

    // Not a [ComputeMethod]!
    public async Task<Chat[]> ListChanged(ChangedChatsQuery query, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var chatsQuery = query.LastId is null
            ? dbContext.Chats.Where(x => x.Version >= query.MinVersion && x.Version <= query.MaxVersion)
            : dbContext.Chats.Where(x => (x.Version > query.MinVersion && x.Version <= query.MaxVersion)
                || (x.Version == query.MinVersion && string.Compare(x.Id, query.LastId.Value) > 0));

        var dbChats = await chatsQuery
            .WhereIf(x => !x.Id.StartsWith(PeerChatId.IdPrefix), query.ExcludePeerChats)
            .WhereIf(x => !x.IsPlaceRootChat, query.ExcludePlaceRootChats)
            .OrderBy(x => x.Version)
            .ThenBy(x => x.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbChats.Select(x => x.ToModel()).ToArray();
    }

    // Not a [ComputeMethod]!
    public async Task<ChatEntry[]> ListChangedEntries(ChangedEntriesQuery query, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        if (query.LastLocalId == 0) {
            var dbEntries = await dbContext.ChatEntries
                .Where(x => x.ChatId == query.ChatId.Value
                    && x.Kind == 0
                    && x.Version >= query.MinVersion
                    && x.Version <= query.MaxVersion)
                .WhereIf(x => x.HasAttachments, query.RequireAttachments)
                .OrderBy(x => x.Version)
                .ThenBy(x => x.LocalId)
                .Take(query.Limit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return await ToChangedModels(query.ChatId, dbEntries, cancellationToken)
                .ConfigureAwait(false);
        }

        var part1 = await dbContext.ChatEntries
            .Where(x => x.ChatId == query.ChatId.Value
                && x.Kind == 0
                && x.Version == query.MinVersion
                && x.LocalId > query.LastLocalId)
            .WhereIf(x => x.HasAttachments, query.RequireAttachments)
            .OrderBy(x => x.Version)
            .ThenBy(x => x.LocalId)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (part1.Count >= query.Limit)
            return await ToChangedModels(query.ChatId, part1, cancellationToken)
                .ConfigureAwait(false);

        var part2 = await dbContext.ChatEntries
            .Where(x => x.ChatId == query.ChatId.Value
                && x.Kind == 0
                && x.Version > query.MinVersion
                && x.Version <= query.MaxVersion)
            .WhereIf(x => x.HasAttachments, query.RequireAttachments)
            .OrderBy(x => x.Version)
            .ThenBy(x => x.LocalId)
            .Take(query.Limit - part1.Count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        part1.AddRange(part2);
        return await ToChangedModels(query.ChatId, part1, cancellationToken)
            .ConfigureAwait(false);
    }

    // Not a [ComputeMethod]!
    public async Task<ChatEntry[]> ListNewEntries(
        ChatId chatId,
        long minLocalIdExclusive,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Callers read a short page as the end of the chat, so the entries of removed accounts
        // dropped from a batch have to be made up for with the next one
        var dbEntries = new List<DbChatEntry>(limit);
        var lastLid = minLocalIdExclusive;
        while (dbEntries.Count < limit) {
            var batchLimit = limit - dbEntries.Count;
            var batch = await dbContext.ChatEntries.Where(x
                    => x.ChatId == chatId.Value
                    && x.Kind == 0
                    && x.LocalId > lastLid
                    && !x.IsRemoved)
                .OrderBy(x => x.LocalId)
                .Take(batchLimit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
                break;

            lastLid = batch[^1].LocalId;
            dbEntries.AddRange(await DropRemovedAuthorEntries(chatId, batch, cancellationToken).ConfigureAwait(false));
            if (batch.Count < batchLimit)
                break;
        }
        var allAttachments = await GetAttachments(dbEntries, cancellationToken).ConfigureAwait(false);
        return dbEntries
            .Select(x => {
                var entryId = ChatEntryId.Parse(x.Id);
                var entryAttachments = allAttachments[entryId];
                return x.ToModel(entryAttachments);
            })
            .ToArray();
    }

    // Private methods

    private async Task<ChatEntry[]> ToChangedModels(
        ChatId chatId, List<DbChatEntry> dbEntries, CancellationToken cancellationToken)
    {
        // Removing an account doesn't touch its entries' versions, so an indexer paging by version
        // would never learn that they went away. Reporting them as removed lets it drop them.
        var removedAuthorSids = await ListRemovedAuthorSids(chatId, dbEntries, cancellationToken)
            .ConfigureAwait(false);
        var result = new ChatEntry[dbEntries.Count];
        for (var i = 0; i < dbEntries.Count; i++) {
            var dbEntry = dbEntries[i];
            var entry = dbEntry.ToModel();
            if (removedAuthorSids.Contains(dbEntry.AuthorId))
                entry = entry with { Flags = entry.Flags | ChatEntryFlags.IsRemoved };
            result[i] = entry;
        }
        return result;
    }
}
