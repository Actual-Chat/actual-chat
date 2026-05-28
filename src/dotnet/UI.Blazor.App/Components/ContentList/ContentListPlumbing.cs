using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

// Shared helpers for content-list components (VisualMediaList / FileList / LinkList).
// All three iterate the same period-skeleton + paged-page protocol; only the item DTO
// and the rendering differ.
internal static class ContentListPlumbing
{
    public const int GridColumns = 3;
    // Window sizes are in blocks (period-pages), not raw items. Block count is the only
    // dimension the UI can reason about without knowing PageSize; spacer sizes use the
    // loaded-block density as a proxy for the unloaded items count.
    // Window always tries to cover at least this many blocks, both for the cold
    // init query and when the user scrolls. Smaller values starve infinite-scroll
    // (one block at a time keeps the end-spacer with skeletons visible too long,
    // especially for kinds with item-level filtering like Link).
    public const int BlocksInWindow = 5;

    public sealed record Block(string PeriodKey, int PageIndex);

    public static List<Block> BuildBlocks(IReadOnlyList<ChatContentPeriod> periods)
    {
        var blocks = new List<Block>();
        foreach (var period in periods)
            for (var p = period.PageCount - 1; p >= 0; p--)
                blocks.Add(new Block(period.PeriodKey, p));
        return blocks;
    }

    public static (int First, int Last) GetWindow(VirtualListDataQuery query, List<Block> blocks)
    {
        int first, last;
        if (query.IsNone)
            (first, last) = (0, 0);
        else {
            var s = FindBlockIndex(blocks, query.KeyRange.Start);
            var e = FindBlockIndex(blocks, query.KeyRange.End);
            if (s < 0 || e < 0)
                (first, last) = (0, 0);
            else {
                first = Math.Max(0, Math.Min(s, e) - 1);
                last = Math.Min(blocks.Count, Math.Max(s, e) + 2);
            }
        }

        // Bidirectional expansion in block units. Same target for cold-init and
        // targeted queries so infinite-scroll grows the window in meaningful chunks.
        while ((last - first) < BlocksInWindow && (last < blocks.Count || first > 0)) {
            if (last < blocks.Count)
                last++;
            else
                first--;
        }
        if (last == first)
            last = Math.Min(blocks.Count, first + 1);
        return (first, last);
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

    public static ContentListItem EmptyPlaceholder()
        => new() { Key = "empty", IsEmptyPlaceholder = true };

    public static VirtualListData<ContentListItem> EmptyData()
        => new([EmptyPlaceholder()]) {
            HasVeryFirstItem = true,
            HasVeryLastItem = true,
        };

    public static long SumVersion(IReadOnlyList<IChatContentItem> items)
    {
        long version = 0;
        foreach (var item in items)
            version += item.Version;
        return version;
    }

    // Chunks one page of items into rows preserving group boundaries. Items must be oldest-first
    // (the order returned by the backend). The returned rows are also oldest-first; callers that
    // render newest-at-top iterate them in reverse.
    public static List<(int RowIndex, string GroupKey, IChatContentItem[] Items)> ChunkPage(
        IReadOnlyList<IChatContentItem> visible,
        int chunkSize,
        ContentGrouping groupBy,
        DateTimeConverter dateTimeConverter)
    {
        var rows = new List<(int, string, IChatContentItem[])>();
        var rowIndex = 0;
        var runStart = 0;
        while (runStart < visible.Count) {
            var groupKey = GetLocalGroup(visible[runStart].At, groupBy, dateTimeConverter).Key;
            var runEnd = runStart;
            while (runEnd < visible.Count
                && GetLocalGroup(visible[runEnd].At, groupBy, dateTimeConverter).Key == groupKey)
                runEnd++;

            // visible is oldest-first and BuildItems renders rows bottom-up, so the
            // short row must lead the run to land at the bottom of the group.
            var runLength = runEnd - runStart;
            var leadSize = runLength % chunkSize;
            if (leadSize == 0)
                leadSize = Math.Min(chunkSize, runLength);
            for (var p = runStart; p < runEnd;) {
                var size = Math.Min(p == runStart ? leadSize : chunkSize, runEnd - p);
                var slice = new IChatContentItem[size];
                for (var i = 0; i < size; i++)
                    slice[i] = visible[p + i];
                rows.Add((rowIndex++, groupKey, slice));
                p += size;
            }
            runStart = runEnd;
        }
        return rows;
    }

    public static string FileExtension(string fileName)
    {
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..].ToLowerInvariant()
            : "";
    }

    // Runs the period-based load pipeline shared by VisualMediaList / FileList / LinkList:
    //   skeleton → window → typed pages → buildItems → VirtualListData with spacer counts.
    // Each component supplies only the typed `loadPage` (which page-method to call) and `buildItems`
    // (which can do extra async work like resolving link entries).
    public static async Task<VirtualListData<ContentListItem>> LoadFromPeriods<TItem>(
        AppUIHub hub,
        Session session,
        ChatId chatId,
        VirtualListDataQuery query,
        VirtualListData<ContentListItem> renderedData,
        Func<string, int, CancellationToken, Task<TItem[]>> loadPage,
        Func<IReadOnlyList<Block>, IReadOnlyList<TItem[]>, CancellationToken, Task<List<ContentListItem>>> buildItems,
        CancellationToken cancellationToken)
        where TItem : IChatContentItem
    {
        // Right panel keeps tabs mounted while collapsed (SideNav just CSS-hides them).
        // Defer all compute/fetch while invisible — VirtualList's ComputeState re-runs
        // once IsVisible flips because Use() registers it as a dependency.
        var isVisible = await hub.PanelsUI.Right.IsVisible.Use(cancellationToken).ConfigureAwait(false);
        if (!isVisible)
            return renderedData;

        // Walk the skeleton cursor: pull more periods only until the current query's
        // window can be served. Backend currently returns everything in one batch
        // (NextPeriodKey=null), so the loop typically runs once; the early-exit kicks
        // in once the backend starts paginating the skeleton.
        var kind = ResolveKind<TItem>();
        var periods = new List<ChatContentPeriod>();
        string? cursor = null;
        do {
            var skeleton = await hub.Chats
                .GetContentPeriods(session, chatId, kind, cursor, cancellationToken)
                .ConfigureAwait(false);
            periods.AddRange(skeleton.Periods);
            cursor = skeleton.NextPeriodKey;
            if (cursor == null)
                break;
            if (HasEnoughForWindow(periods, query))
                break;
        } while (true);
        if (periods.Count == 0)
            return EmptyData();

        var blocks = BuildBlocks(periods);
        var (first, last) = GetWindow(query, blocks);
        var windowBlocks = blocks.GetRange(first, last - first);
        var contents = await windowBlocks
            .Select(b => loadPage(b.PeriodKey, b.PageIndex, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var listItems = await buildItems(windowBlocks, contents, cancellationToken).ConfigureAwait(false);

        if (listItems.Count == 0)
            return EmptyData();

        var result = new VirtualListData<ContentListItem>(listItems) {
            Index = renderedData.Index + 1,
            // BuildItems emits newest-first, so the rendered DOM order is newest-at-top.
            // VirtualList treats Items[0] as VeryFirst — keep it aligned with our window.
            HasVeryFirstItem = first == 0,
            HasVeryLastItem = last == blocks.Count,
        };
        return result.IsSimilarTo(renderedData) ? renderedData : result;
    }

    private static bool HasEnoughForWindow(List<ChatContentPeriod> periods, VirtualListDataQuery query)
    {
        if (query.IsNone) {
            var blockCount = 0;
            foreach (var p in periods)
                blockCount += p.PageCount;
            return blockCount >= BlocksInWindow;
        }
        // Targeted query: stop once both endpoints map to already-loaded blocks.
        var blocks = BuildBlocks(periods);
        var s = FindBlockIndex(blocks, query.KeyRange.Start);
        var e = FindBlockIndex(blocks, query.KeyRange.End);
        return s >= 0 && e >= 0;
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
