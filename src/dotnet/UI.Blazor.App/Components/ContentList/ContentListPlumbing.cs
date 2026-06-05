using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

// Shared plumbing for content-list components (VisualMediaList / FileList / LinkList).
// All three speak the same period-skeleton + paged-page protocol; here lives the
// rows-from-anchor windowing that decides which blocks to fetch and crops the rendered
// list to exactly what VirtualList asked for.
internal static class ContentListPlumbing
{
    // Cold-init target: how many rows the very first GetData call returns when there
    // is no anchor and no MoveRange. Subsequent scroll requests are sized by MoveRange.
    public const int TargetRowsColdInit = 25;

    public sealed record Block(string PeriodKey, int PageIndex);

    private sealed record RowSpec<TItem>(
        string Key, string GroupKey, string GroupTitle, string VisorDate, TItem[] Items)
        where TItem : IChatContentItem;

    public static List<Block> BuildBlocks(IReadOnlyList<ChatContentPeriod> periods)
    {
        var blocks = new List<Block>();
        foreach (var period in periods)
            for (var p = period.PageCount - 1; p >= 0; p--)
                blocks.Add(new Block(period.PeriodKey, p));
        return blocks;
    }

    public static int FindBlockIndex(List<Block> blocks, string key)
    {
        var parts = key.Split(':');
        if (parts.Length < 4 || (parts[0] != "r" && parts[0] != "i"))
            return -1;
        if (!int.TryParse(parts[2], out var pageIndex))
            return -1;

        return blocks.FindIndex(b => b.PeriodKey == parts[1] && b.PageIndex == pageIndex);
    }

    public static bool IsRowKey(string key)
        => key.Length > 2 && key[1] == ':' && (key[0] == 'r' || key[0] == 'i');

    public static (string Key, string Title) GetLocalGroup(
        Moment at,
        ContentGrouping groupBy,
        DateTimeConverter dateTimeConverter)
    {
        var d = dateTimeConverter.ToLocalTime(at);
        return groupBy switch {
            ContentGrouping.Day => ($"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}", d.ToString("MMMM d, yyyy")),
            ContentGrouping.Month => ($"{d.Year:D4}-{d.Month:D2}", d.ToString("MMMM yyyy")),
            _ => ("", ""),
        };
    }

    // Day-level label shown by the floating date-visor — finer than the month group headers.
    public static string GetVisorDate(Moment at, DateTimeConverter dateTimeConverter)
        => dateTimeConverter.ToLocalTime(at).ToString("d MMMM yyyy");

    public static ContentListItem EmptyPlaceholder()
        => new() { Key = "empty", IsEmptyPlaceholder = true };

    public static VirtualListData<ContentListItem> EmptyData()
        => new([EmptyPlaceholder()]) {
            HasVeryFirstItem = true,
            HasVeryLastItem = true,
        };

    public static ContentListItem GroupHeader(string key, string title)
        => new() { Key = $"g:{key}", IsGroup = true, GroupTitle = title };

    public static long SumVersion(IReadOnlyList<IChatContentItem> items)
    {
        long version = 0;
        foreach (var item in items)
            version += item.Version;
        return version;
    }

    public static string FileExtension(string fileName)
    {
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..].ToLowerInvariant()
            : "";
    }

    // Rows-from-anchor + crop pipeline shared by VisualMediaList / FileList / LinkList.
    //
    // 1. Resolves wantBefore/wantAfter from VirtualList's MoveRange (or the cold-init
    //    target when query.IsNone).
    // 2. Loads the period skeleton (currently always one batch — backend returns the
    //    whole skeleton with NextPeriodKey=null; the cursor loop is forward-compatible
    //    for paginated skeletons).
    // 3. Locates seed blocks via KeyRange (or [0..0] for cold init) and loads them.
    // 4. Expands the loaded block range one block at a time toward the side that lacks
    //    rows, until rowsBefore ≥ wantBefore and rowsAfter ≥ wantAfter (or we run out
    //    of blocks).
    // 5. Crops the flattened row list to exactly wantBefore + seed + wantAfter rows,
    //    so the returned VirtualListData mirrors what was asked — no fat windows.
    // 6. Optional prefetch hook warms extra data per visible window (LinkList uses it
    //    to fetch ChatEntry for rich link previews).
    // 7. rowFactory wraps each row's items into ContentListItem; group headers are
    //    inserted on local-group boundaries.
    public static async Task<VirtualListData<ContentListItem>> LoadFromPeriods<TItem>(
        AppUIHub hub,
        Session session,
        ChatId chatId,
        VirtualListDataQuery query,
        VirtualListData<ContentListItem> renderedData,
        int rowSize,
        ContentGrouping groupBy,
        Func<Block, CancellationToken, Task<TItem[]>> loadPage,
        Func<TItem[], string, string, ContentListItem> rowFactory,
        Func<IReadOnlyList<TItem>, CancellationToken, Task>? prefetch,
        CancellationToken cancellationToken)
        where TItem : IChatContentItem
    {
        // Right panel keeps tabs mounted while collapsed (SideNav just CSS-hides them).
        // Defer all compute/fetch while invisible — VirtualList's ComputeState re-runs
        // once IsVisible flips because Use() registers it as a dependency.
        var isVisible = await hub.PanelsUI.Right.IsVisible.Use(cancellationToken).ConfigureAwait(false);
        if (!isVisible)
            return renderedData;

        var dateTimeConverter = hub.DateTimeConverter;
        var kind = ResolveKind<TItem>();

        // 1. Translate the VirtualList query into row-space terms.
        // VirtualList resets Query to None after every render, so query.IsNone alone
        // can't tell cold-init from a Fusion-driven recompute of an already-rendered
        // list. Use renderedData as the tiebreaker — same pattern as ChatView's
        // (false, false) vs (false, true) switch in GetChatDataQuery.
        int wantBefore, wantAfter;
        string? seedStartKey, seedEndKey;
        if (query.IsNone) {
            var firstRow = renderedData.FirstItem;
            var lastRow = renderedData.LastItem;
            var isRehydrating = firstRow is { IsEmptyPlaceholder: false }
                && lastRow is { IsEmptyPlaceholder: false }
                && IsRowKey(firstRow.Key)
                && IsRowKey(lastRow.Key);
            if (isRehydrating) {
                wantBefore = 0;
                wantAfter = 0;
                seedStartKey = firstRow!.Key;
                seedEndKey = lastRow!.Key;
            }
            else {
                wantBefore = 0;
                wantAfter = TargetRowsColdInit;
                seedStartKey = seedEndKey = null;
            }
        }
        else {
            wantBefore = Math.Max(0, -query.MoveRange.Start);
            wantAfter = Math.Max(0, query.MoveRange.End);
            seedStartKey = query.KeyRange.Start;
            seedEndKey = query.KeyRange.End;
        }

        // 2. Skeleton (lazy). Pull the first page; pull more only when needed —
        // either the anchor wasn't found in what we have, or block-expansion ran
        // out of room. Backend currently returns the whole skeleton in one page
        // (cursor stays null after the first call), so the extra pulls are no-ops
        // today; the loop is correct once backend starts paginating.
        var periods = new List<ChatContentPeriod>();
        string? cursor = null;
        // Returns true if THIS call added periods. Callers gate further pulls on
        // `cursor != null` (no need to re-check inside the helper).
        async Task<bool> PullNextSkeletonPage()
        {
            var page = await hub.Chats
                .GetContentPeriods(session, chatId, kind, cursor, cancellationToken)
                .ConfigureAwait(false);
            periods.AddRange(page.Periods);
            cursor = page.NextPeriodKey;
            return page.Periods.Length > 0;
        }

        // 3. Skeleton head. A chat with no content in the current calendar year
        // but older history returns Periods=[] with NextPeriodKey != null on the
        // first page — keep pulling until we see real periods or the skeleton
        // is exhausted.
        do {
            await PullNextSkeletonPage().ConfigureAwait(false);
        } while (periods.Count == 0 && cursor != null);
        if (periods.Count == 0)
            return EmptyData();

        // 4. Locate seed: pull more skeleton pages until the anchor blocks are
        // found (or skeleton is exhausted). Cold init only needs the first page —
        // blocks[0] = newest block. Backend never returns periods with PageCount=0,
        // so we don't guard against an empty blocks list here.
        List<Block> blocks;
        int first, last;
        while (true) {
            blocks = BuildBlocks(periods);
            if (seedStartKey == null) {
                first = last = 0;
                break;
            }
            var s = FindBlockIndex(blocks, seedStartKey);
            var e = FindBlockIndex(blocks, seedEndKey!);
            if (s >= 0 && e >= 0) {
                first = Math.Min(s, e);
                last = Math.Max(s, e);
                break;
            }
            if (cursor != null) {
                // Anchor may live in a not-yet-pulled skeleton page.
                await PullNextSkeletonPage().ConfigureAwait(false);
                continue;
            }
            // Skeleton exhausted, anchor genuinely stale.
            seedStartKey = seedEndKey = null;
            wantBefore = 0;
            first = last = 0;
            break;
        }

        // 5. Load seed blocks; index seed positions inside their blocks so we can
        // seed rowsBefore/rowsAfter counters without flattening.
        var blockRows = new Dictionary<int, List<RowSpec<TItem>>>();
        for (var i = first; i <= last; i++) {
            var block = blocks[i];
            var items = await loadPage(block, cancellationToken).ConfigureAwait(false);
            blockRows[i] = BuildBlockRowsNewestFirst(items, block, rowSize, groupBy, dateTimeConverter);
        }

        int rowsBefore, rowsAfter;
        int seedStartPos = -1, seedEndPos = -1;
        if (seedStartKey == null) {
            rowsBefore = 0;
            rowsAfter = 0;
            for (var i = first; i <= last; i++)
                rowsAfter += blockRows[i].Count;
        }
        else {
            seedStartPos = blockRows[first].FindIndex(r => r.Key == seedStartKey);
            seedEndPos = blockRows[last].FindIndex(r => r.Key == seedEndKey);
            if (seedStartPos < 0 || seedEndPos < 0) {
                // Anchor disappeared mid-flight (e.g. seed block invalidated after locateSeed).
                // Degrade to cold-init shape, keep wantAfter.
                seedStartKey = seedEndKey = null;
                wantBefore = 0;
                rowsBefore = 0;
                rowsAfter = 0;
                for (var i = first; i <= last; i++)
                    rowsAfter += blockRows[i].Count;
            }
            else {
                // rowsBefore = rows ABOVE seedStart inside the loaded window — only
                // those in blockRows[first] above seedStartPos. Anything below
                // seedStart (inside seedFirst, between seed blocks, inside seedLast
                // up to seedEnd) belongs to the seed zone, not to rowsBefore/After.
                // rowsAfter symmetrically: only rows below seedEnd in blockRows[last].
                rowsBefore = seedStartPos;
                rowsAfter = blockRows[last].Count - 1 - seedEndPos;
            }
        }

        // 6. ExpandNewer — pure backward. Skeleton pagination only grows older,
        // so the newer edge is whatever the very first skeleton page provided.
        async Task ExpandNewer()
        {
            while (rowsBefore < wantBefore && first > 0) {
                first--;
                var items = await loadPage(blocks[first], cancellationToken).ConfigureAwait(false);
                blockRows[first] = BuildBlockRowsNewestFirst(items, blocks[first], rowSize, groupBy, dateTimeConverter);
                rowsBefore += blockRows[first].Count;
            }
        }

        // 7. ExpandOlder — forward with lazy skeleton extension.
        async Task ExpandOlder()
        {
            while (rowsAfter < wantAfter) {
                if (last == blocks.Count - 1) {
                    if (cursor == null || !await PullNextSkeletonPage().ConfigureAwait(false))
                        return;
                    blocks = BuildBlocks(periods);
                    if (last == blocks.Count - 1)
                        return; // skeleton didn't actually grow
                }
                last++;
                var items = await loadPage(blocks[last], cancellationToken).ConfigureAwait(false);
                blockRows[last] = BuildBlockRowsNewestFirst(items, blocks[last], rowSize, groupBy, dateTimeConverter);
                rowsAfter += blockRows[last].Count;
            }
        }

        await ExpandNewer().ConfigureAwait(false);
        await ExpandOlder().ConfigureAwait(false);

        // 8. Flatten once at the end and crop to the requested window.
        var flat = new List<RowSpec<TItem>>(rowsBefore + rowsAfter + (seedEndPos - seedStartPos + 1));
        for (var i = first; i <= last; i++)
            flat.AddRange(blockRows[i]);

        // All loaded blocks ended up empty — happens when a concurrent delete
        // drained the period after the skeleton was computed but before we
        // loaded its pages. Bail to the empty placeholder; next compute will
        // re-render once skeleton catches up.
        if (flat.Count == 0)
            return EmptyData();

        int cropStart, cropEnd;
        if (seedStartKey == null) {
            cropStart = 0;
            cropEnd = Math.Min(flat.Count, wantAfter) - 1;
        }
        else {
            var seedStartIdx = rowsBefore;                  // rows above seedStart in the loaded window
            var seedEndIdx = flat.Count - 1 - rowsAfter;    // rows below seedEnd in the loaded window
            cropStart = Math.Max(0, seedStartIdx - wantBefore);
            cropEnd = Math.Min(flat.Count - 1, seedEndIdx + wantAfter);
        }
        if (cropEnd < cropStart)
            cropEnd = cropStart;
        var visible = flat.GetRange(cropStart, cropEnd - cropStart + 1);

        // 7. Prefetch extra data for visible rows (LinkList warms ChatEntry resolution).
        if (prefetch != null) {
            var visibleItems = new List<TItem>(visible.Count * rowSize);
            foreach (var r in visible)
                visibleItems.AddRange(r.Items);
            if (visibleItems.Count > 0)
                await prefetch(visibleItems, cancellationToken).ConfigureAwait(false);
        }

        // 8. Assemble ContentListItem list with group headers on local-group boundaries.
        var listItems = new List<ContentListItem>(visible.Count + 4);
        string? currentGroupKey = null;
        foreach (var r in visible) {
            if (r.GroupKey != currentGroupKey) {
                currentGroupKey = r.GroupKey;
                if (r.GroupTitle.Length > 0)
                    listItems.Add(GroupHeader(r.GroupKey, r.GroupTitle));
            }
            listItems.Add(rowFactory(r.Items, r.Key, r.VisorDate));
        }

        if (listItems.Count == 0)
            return EmptyData();

        var hasVeryFirstItem = first == 0 && cropStart == 0;
        var hasVeryLastItem = last == blocks.Count - 1 && cursor == null && cropEnd == flat.Count - 1;

        var result = new VirtualListData<ContentListItem>(listItems) {
            Index = renderedData.Index + 1,
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
        };
        var isSame = result.IsSimilarTo(renderedData);

#if false
        // Quick window/query diagnostics. Disabled in source so it doesn't spam
        // DevLog; flip the #if to re-enable when debugging windowing/crop math.
        var log = hub.LogFor(typeof(ContentListPlumbing));
        var winFirst = $"{blocks[first].PeriodKey}:{blocks[first].PageIndex}";
        var winLast = $"{blocks[last].PeriodKey}:{blocks[last].PageIndex}";
        var itemFirst = listItems.Count > 0 ? listItems[0].Key : "-";
        var itemLast = listItems.Count > 0 ? listItems[^1].Key : "-";
        if (query.IsNone)
            log.LogWarning(
                "CL[{Kind}/{ChatId}] same={IsSame}\n"
                + "  q=<none>\n"
                + "  win={{ blocks: [{First}..{Last}]/{Total}, first: {WinFirst}, last: {WinLast}, hasFirst: {HasVeryFirst}, hasLast: {HasVeryLast} }}\n"
                + "  items={{ count: {ItemCount}, first: {ItemFirst}, last: {ItemLast}, rowsFlat: {RowsFlat}, crop: [{CropStart}..{CropEnd}] }}",
                kind, chatId, isSame, first, last, blocks.Count, winFirst, winLast,
                result.HasVeryFirstItem, result.HasVeryLastItem,
                listItems.Count, itemFirst, itemLast, flat.Count, cropStart, cropEnd);
        else
            log.LogWarning(
                "CL[{Kind}/{ChatId}] same={IsSame}\n"
                + "  q={{ keys: {KeyRange}, virt: {VirtualRange}, move: {MoveRange} }}\n"
                + "  win={{ blocks: [{First}..{Last}]/{Total}, first: {WinFirst}, last: {WinLast}, hasFirst: {HasVeryFirst}, hasLast: {HasVeryLast} }}\n"
                + "  items={{ count: {ItemCount}, first: {ItemFirst}, last: {ItemLast}, rowsFlat: {RowsFlat}, seed: [{SeedStart}..{SeedEnd}], crop: [{CropStart}..{CropEnd}] }}",
                kind, chatId, isSame, query.KeyRange, query.VirtualRange, query.MoveRange,
                first, last, blocks.Count, winFirst, winLast,
                result.HasVeryFirstItem, result.HasVeryLastItem,
                listItems.Count, itemFirst, itemLast, flat.Count, rowsBefore, flat.Count - 1 - rowsAfter, cropStart, cropEnd);
#endif

        return isSame ? renderedData : result;
    }

    // Slices one page of items into rows preserving group boundaries, emitting in
    // newest-first DOM order. Within each local-group run the short row leads so that
    // after the reverse it lands at the bottom of the group — matching ChunkPage's
    // original layout. Items must arrive oldest-first (as backend returns them).
    private static List<RowSpec<TItem>> BuildBlockRowsNewestFirst<TItem>(
        TItem[] items,
        Block block,
        int rowSize,
        ContentGrouping groupBy,
        DateTimeConverter dateTimeConverter)
        where TItem : IChatContentItem
    {
        var keyPrefix = rowSize == 1 ? "i" : "r";
        var rows = new List<RowSpec<TItem>>();
        var rowIndex = 0;
        var runStart = 0;
        while (runStart < items.Length) {
            var firstGroupKey = GetLocalGroup(items[runStart].At, groupBy, dateTimeConverter).Key;
            var runEnd = runStart;
            while (runEnd < items.Length
                && GetLocalGroup(items[runEnd].At, groupBy, dateTimeConverter).Key == firstGroupKey)
                runEnd++;

            var runLength = runEnd - runStart;
            var leadSize = runLength % rowSize;
            if (leadSize == 0)
                leadSize = Math.Min(rowSize, runLength);
            var p = runStart;
            while (p < runEnd) {
                var size = Math.Min(p == runStart ? leadSize : rowSize, runEnd - p);
                // Items within a row are stored newest-first to match the DOM read order
                // (newest at the left for multi-item rows); single-item rows are unaffected.
                var slice = new TItem[size];
                for (var i = 0; i < size; i++)
                    slice[i] = items[p + size - 1 - i];
                var rowGroup = GetLocalGroup(slice[^1].At, groupBy, dateTimeConverter);
                rows.Add(new RowSpec<TItem>(
                    $"{keyPrefix}:{block.PeriodKey}:{block.PageIndex}:{rowIndex}",
                    rowGroup.Key,
                    rowGroup.Title,
                    GetVisorDate(slice[0].At, dateTimeConverter),
                    slice));
                p += size;
                rowIndex++;
            }
            runStart = runEnd;
        }
        rows.Reverse();
        return rows;
    }

    private static ChatContentKind ResolveKind<TItem>() where TItem : IChatContentItem
    {
        if (typeof(TItem) == typeof(VisualMediaItem))
            return ChatContentKind.Media;
        if (typeof(TItem) == typeof(FileItem))
            return ChatContentKind.File;
        if (typeof(TItem) == typeof(LinkItem))
            return ChatContentKind.Link;
        throw new ArgumentOutOfRangeException(
            nameof(TItem), typeof(TItem), $"Unknown content item type: {typeof(TItem).Name}");
    }
}
