using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

// Read + write side of the chat content index (Photo/Video/File/Link tabs in the
// right panel). Lives separately from the rest of ChatsBackend because the
// surface area is self-contained: the period skeleton + paged page-load read
// path on top, the index-update commands + invalidation in the middle, and the
// shared helpers below.
public partial class ChatsBackend
{
    // [ComputeMethod]
    public virtual async Task<ChatContentSkeleton> GetContentPeriods(
        ChatId chatId,
        ChatContentKind kind,
        string? beforePeriodKey,
        CancellationToken cancellationToken)
    {
        // Skeleton is paged one calendar year at a time. First call
        // (beforePeriodKey=null) returns Jan..Dec of the current UTC year;
        // subsequent calls send back the previous NextPeriodKey to advance one
        // calendar year into the past.
        const int monthsPerPage = 12;

        DateTime upperBoundExclusive;
        if (beforePeriodKey != null) {
            var (start, _) = ParseUtcMonthRange(beforePeriodKey);
            upperBoundExclusive = start;
        }
        else {
            var nowUtc = Clocks.SystemClock.Now.ToDateTime();
            upperBoundExclusive = new DateTime(nowUtc.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
        var lowerBoundInclusive = upperBoundExclusive.AddMonths(-monthsPerPage);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var chatSid = chatId.Value;
        var months = kind switch {
            ChatContentKind.Media => await QueryPeriodCounts(
                dbContext.ChatVisualMediaItems, chatSid, lowerBoundInclusive, upperBoundExclusive, cancellationToken)
                .ConfigureAwait(false),
            ChatContentKind.File => await QueryPeriodCounts(
                dbContext.ChatFileItems, chatSid, lowerBoundInclusive, upperBoundExclusive, cancellationToken)
                .ConfigureAwait(false),
            ChatContentKind.Link => await QueryPeriodCounts(
                dbContext.ChatLinkItems, chatSid, lowerBoundInclusive, upperBoundExclusive, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var periods = months
            .Select(m => new ChatContentPeriod {
                PeriodKey = FormatUtcMonthKey(m.Year, m.Month),
                PageCount = (m.Count + ChatContentPeriod.PageSize - 1) / ChatContentPeriod.PageSize,
            })
            .OrderByDescending(p => p.PeriodKey)
            .ToArray();
        // NextPeriodKey = the month immediately at the lower boundary; client
        // sends it back as beforePeriodKey to advance to the next year window.
        // Pulled out as a separate compute method so invalidation can target it
        // independently of the in-window month list.
        var hasOlder = await HasContentBefore(chatId, kind, lowerBoundInclusive, cancellationToken).ConfigureAwait(false);
        var nextPeriodKey = hasOlder
            ? FormatUtcMonthKey(lowerBoundInclusive.Year, lowerBoundInclusive.Month)
            : null;
        return new ChatContentSkeleton { Periods = periods, NextPeriodKey = nextPeriodKey };
    }

    // [ComputeMethod]
    public virtual async Task<VisualMediaItem[]> GetVisualMediaPeriod(
        ChatId chatId,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbItems = await QueryPeriodPage(
                dbContext.ChatVisualMediaItems, chatId.Value, periodKey, pageIndex, cancellationToken)
            .ConfigureAwait(false);
        return dbItems.Select(x => x.ToModel()).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<FileItem[]> GetFilePeriod(
        ChatId chatId,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbItems = await QueryPeriodPage(
                dbContext.ChatFileItems, chatId.Value, periodKey, pageIndex, cancellationToken)
            .ConfigureAwait(false);
        return dbItems.Select(x => x.ToModel()).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<LinkItem[]> GetLinkPeriod(
        ChatId chatId,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbItems = await QueryPeriodPage(
                dbContext.ChatLinkItems, chatId.Value, periodKey, pageIndex, cancellationToken)
            .ConfigureAwait(false);
        if (dbItems.Count == 0)
            return [];
        return await dbItems
            .Select(x => ResolveLinkItem(x.ToModel(), cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual Task OnUpdateChatVisualMediaIndex(
        ChatsBackend_UpdateChatVisualMediaIndex command,
        CancellationToken cancellationToken)
    {
        var (chatId, entryIds, items) = command;
        return UpdateContentIndex<VisualMediaItem, DbChatVisualMediaItem>(
            ChatContentKind.Media,
            chatId,
            entryIds,
            items,
            db => db.ChatVisualMediaItems,
            x => new DbChatVisualMediaItem(x with { Version = VersionGenerator.NextVersion() }),
            cancellationToken);
    }

    // [CommandHandler]
    public virtual Task OnUpdateChatFileIndex(
        ChatsBackend_UpdateChatFileIndex command,
        CancellationToken cancellationToken)
    {
        var (chatId, entryIds, items) = command;
        return UpdateContentIndex<FileItem, DbChatFileItem>(
            ChatContentKind.File,
            chatId,
            entryIds,
            items,
            db => db.ChatFileItems,
            x => new DbChatFileItem(x with { Version = VersionGenerator.NextVersion() }),
            cancellationToken);
    }

    // [CommandHandler]
    public virtual Task OnUpdateChatLinkIndex(
        ChatsBackend_UpdateChatLinkIndex command,
        CancellationToken cancellationToken)
    {
        var (chatId, entryIds, items) = command;
        return UpdateContentIndex<LinkItem, DbChatLinkItem>(
            ChatContentKind.Link,
            chatId,
            entryIds,
            items,
            db => db.ChatLinkItems,
            x => new DbChatLinkItem(x with { Version = VersionGenerator.NextVersion() }),
            cancellationToken);
    }

    // Protected members

    // [ComputeMethod]

    protected virtual async Task<bool> HasContentBefore(
        ChatId chatId,
        ChatContentKind kind,
        DateTime boundaryUtc,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var chatSid = chatId.Value;
        return kind switch {
            ChatContentKind.Media => await QueryHasContentBefore(
                dbContext.ChatVisualMediaItems, chatSid, boundaryUtc, cancellationToken).ConfigureAwait(false),
            ChatContentKind.File => await QueryHasContentBefore(
                dbContext.ChatFileItems, chatSid, boundaryUtc, cancellationToken).ConfigureAwait(false),
            ChatContentKind.Link => await QueryHasContentBefore(
                dbContext.ChatLinkItems, chatSid, boundaryUtc, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    // Private members

    private async Task<LinkItem> ResolveLinkItem(LinkItem item, CancellationToken cancellationToken)
    {
        if (item.LinkPreviewId.IsEmpty)
            return item;

        var linkPreview = await LinkPreviewsBackend.Get(item.LinkPreviewId, false, cancellationToken).ConfigureAwait(false);
        return item with { LinkPreview = linkPreview };
    }

    private async Task UpdateContentIndex<TItem, TDbItem>(
        ChatContentKind kind,
        ChatId chatId,
        ChatEntryId[] entryIds,
        TItem[] items,
        Func<ChatDbContext, DbSet<TDbItem>> getTable,
        Func<TItem, TDbItem> toDbItem,
        CancellationToken cancellationToken)
        where TItem : IChatContentItem
        where TDbItem : class, IDbChatContentItem
    {
        if (entryIds.Length == 0)
            return;

        if (Invalidation.IsActive) {
            InvalidateContentIndex(kind, chatId);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var table = getTable(dbContext);
        var entrySids = entryIds.Select(x => x.Value).Distinct().ToList();
        var deletedAts = await table
            .Where(x => entrySids.Contains(x.EntryId))
            .Select(x => x.At)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await table
            .Where(x => entrySids.Contains(x.EntryId))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        foreach (var item in items)
            dbContext.Add(toDbItem(item));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var chatSid = chatId.Value;
        var affectedMonths = CollectAffectedMonths(deletedAts, items);
        var pageCounts = new Dictionary<string, int>();
        foreach (var monthKey in affectedMonths) {
            var (periodStart, periodEnd) = ParseUtcMonthRange(monthKey);
            pageCounts[monthKey] = await table
                .Where(x => x.ChatId == chatSid && x.At >= periodStart && x.At < periodEnd)
                .CountAsync(cancellationToken).ConfigureAwait(false);
        }
        CommandContext.GetCurrent().Operation.Items
            .KeylessSet(new ContentIndexPageCounts(kind, pageCounts));
    }

    private static HashSet<string> CollectAffectedMonths<TItem>(
        IEnumerable<DateTime> deletedAts,
        IEnumerable<TItem> insertedItems)
        where TItem : IChatContentItem
    {
        var months = new HashSet<string>();
        foreach (var at in deletedAts)
            months.Add(FormatUtcMonthKey(at.Year, at.Month));
        foreach (var item in insertedItems) {
            var at = item.At.ToDateTime();
            months.Add(FormatUtcMonthKey(at.Year, at.Month));
        }
        return months;
    }

    private void InvalidateContentIndex(ChatContentKind kind, ChatId chatId)
    {
        var pageCounts = CommandContext.GetCurrent().Operation.Items
            .KeylessGet<ContentIndexPageCounts>();
        if (pageCounts == null || pageCounts.Kind != kind) {
            // No per-month info — invalidate just the first skeleton page conservatively.
            _ = GetContentPeriods(chatId, kind, null, default);
            return;
        }

        var nowUtc = Clocks.SystemClock.Now.ToDateTime();
        var nowYear = nowUtc.Year;

        // Skeleton page that owns an affected month m: beforeKey = null when
        // m sits in the current year, otherwise the first month of (m.year+1).
        // Each month belongs to exactly one page — invalidate that one for its
        // Periods/PageCount entry.
        var skeletonBeforeKeys = new HashSet<string?>();
        foreach (var monthKey in pageCounts.PageCounts.Keys) {
            var (monthStart, _) = ParseUtcMonthRange(monthKey);
            skeletonBeforeKeys.Add(monthStart.Year == nowYear
                ? null
                : FormatUtcMonthKey(monthStart.Year + 1, 1));
        }
        foreach (var beforeKey in skeletonBeforeKeys)
            _ = GetContentPeriods(chatId, kind, beforeKey, default);

        // NextPeriodKey on every skeleton page comes from HasContentBefore(b),
        // where b = page lower bound. An item at month m can flip HasContentBefore(b)
        // iff b > m — i.e., for year boundaries y in (m.year, nowYear].
        // Invalidating those compute keys cascades into the corresponding
        // GetContentPeriods(beforeKey) entries automatically.
        var boundaries = new HashSet<DateTime>();
        foreach (var monthKey in pageCounts.PageCounts.Keys) {
            var (monthStart, _) = ParseUtcMonthRange(monthKey);
            for (var y = monthStart.Year + 1; y <= nowYear; y++)
                boundaries.Add(new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        foreach (var b in boundaries)
            _ = HasContentBefore(chatId, kind, b, default);

        foreach (var (monthKey, count) in pageCounts.PageCounts) {
            var pageCount = Math.Max(1, (count + ChatContentPeriod.PageSize - 1) / ChatContentPeriod.PageSize);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++) {
                switch (kind) {
                case ChatContentKind.Media:
                    _ = GetVisualMediaPeriod(chatId, monthKey, pageIndex, default);
                    break;
                case ChatContentKind.File:
                    _ = GetFilePeriod(chatId, monthKey, pageIndex, default);
                    break;
                case ChatContentKind.Link:
                    _ = GetLinkPeriod(chatId, monthKey, pageIndex, default);
                    break;
                }
            }
        }
    }

    // Aggregates content items into per-month counts for the given UTC window.
    // Shared by Media/File/Link — tables implement IDbChatContentItem so the
    // same LINQ shape compiles for all three.
    private static async Task<List<(int Year, int Month, int Count)>>
        QueryPeriodCounts<TDbItem>(
            IQueryable<TDbItem> table,
            string chatSid,
            DateTime lowerBoundInclusive,
            DateTime upperBoundExclusive,
            CancellationToken cancellationToken)
        where TDbItem : class, IDbChatContentItem
        => (await table
                .Where(x => x.ChatId == chatSid && x.At >= lowerBoundInclusive && x.At < upperBoundExclusive)
                .GroupBy(x => new { x.At.Year, x.At.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(m => (m.Year, m.Month, m.Count))
            .ToList();

    private static Task<bool> QueryHasContentBefore<TDbItem>(
        IQueryable<TDbItem> table,
        string chatSid,
        DateTime boundaryUtc,
        CancellationToken cancellationToken)
        where TDbItem : class, IDbChatContentItem
        => table.AnyAsync(x => x.ChatId == chatSid && x.At < boundaryUtc, cancellationToken);

    // Loads a paged slice of chat content items for the given UTC month, oldest-first
    // by (At, EntryLocalId, LocalIndex). Shared by Get{VisualMedia,File,Link}Period.
    private static async Task<List<TDbItem>> QueryPeriodPage<TDbItem>(
        IQueryable<TDbItem> table,
        string chatSid,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken)
        where TDbItem : class, IDbChatContentItem
    {
        var (periodStart, periodEnd) = ParseUtcMonthRange(periodKey);
        return await table
            .Where(x => x.ChatId == chatSid && x.At >= periodStart && x.At < periodEnd)
            .OrderBy(x => x.At)
            .ThenBy(x => x.EntryLocalId)
            .ThenBy(x => x.LocalIndex)
            .Skip(pageIndex * ChatContentPeriod.PageSize)
            .Take(ChatContentPeriod.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string FormatUtcMonthKey(int year, int month)
        => $"{year:D4}-{month:D2}";

    private static (DateTime Start, DateTime End) ParseUtcMonthRange(string periodKey)
    {
        var year = int.Parse(periodKey.AsSpan(0, 4));
        var month = int.Parse(periodKey.AsSpan(5, 2));
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }

    // Nested types
    private sealed record ContentIndexPageCounts(ChatContentKind Kind, Dictionary<string, int> PageCounts);
}
