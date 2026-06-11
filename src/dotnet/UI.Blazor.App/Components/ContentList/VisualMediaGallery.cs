using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;

namespace ActualChat.UI.Blazor.App.Components;

// Stateful flat windowed view over a chat's visual-media library for the media viewer.
// Reuses the period-skeleton + paged-page protocol (and the block helpers) that the
// right-panel grid uses, but yields a flat newest-first sequence (index 0 = newest)
// and tracks an exposed window the viewer extends at either edge.
public sealed class VisualMediaGallery(AppUIHub hub, Session session, ChatId chatId)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<ChatContentPeriod> _periods = new();
    private readonly Dictionary<int, VisualMediaItem[]> _blockItems = new();
    private readonly Dictionary<ChatEntryId, ChatEntry?> _entryCache = new();
    private readonly List<VisualMediaItem> _loaded = new();
    private List<ContentListPlumbing.Block> _blocks = new();
    private string? _cursor;
    private bool _skeletonStarted;
    private int _firstLoadedBlock = -1;
    private int _lastLoadedBlock = -1;
    private int _exposedStart;
    private int _exposedEnd;

    private IChats Chats => hub.Chats;
    private bool HasNewer => _exposedStart > 0 || _firstLoadedBlock > 0;
    private bool HasOlder => _exposedEnd < _loaded.Count || _lastLoadedBlock < _blocks.Count - 1 || _cursor != null;

    public async Task<GalleryInit> InitializeAround(VisualMediaItem anchor, int radius, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await EnsureSkeletonHead(cancellationToken).ConfigureAwait(false);
            var (anchorBlock, anchorPos) = await LocateAnchor(anchor, cancellationToken).ConfigureAwait(false);
            if (anchorBlock < 0)
                return new GalleryInit([anchor], 0, false, false);

            _firstLoadedBlock = _lastLoadedBlock = anchorBlock;
            _loaded.Clear();
            _loaded.AddRange(_blockItems[anchorBlock]);
            var anchorIndex = anchorPos;
            while (anchorIndex < radius && _firstLoadedBlock > 0) {
                var block = await EnsureBlockLoaded(_firstLoadedBlock - 1, cancellationToken).ConfigureAwait(false);
                _firstLoadedBlock--;
                _loaded.InsertRange(0, block);
                anchorIndex += block.Length;
            }
            while (_loaded.Count - 1 - anchorIndex < radius) {
                if (!await TryLoadOlderBlock(cancellationToken).ConfigureAwait(false))
                    break;
            }

            _exposedStart = Math.Max(0, anchorIndex - radius);
            _exposedEnd = Math.Min(_loaded.Count, anchorIndex + radius + 1);
            var window = _loaded.GetRange(_exposedStart, _exposedEnd - _exposedStart);
            return new GalleryInit(window, anchorIndex - _exposedStart, HasNewer, HasOlder);
        }
        finally {
            _lock.Release();
        }
    }

    public async Task<GalleryPage> LoadNewer(int count, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var revealed = new List<VisualMediaItem>();
            while (revealed.Count < count) {
                if (_exposedStart == 0) {
                    if (_firstLoadedBlock <= 0)
                        break;
                    var block = await EnsureBlockLoaded(_firstLoadedBlock - 1, cancellationToken).ConfigureAwait(false);
                    _firstLoadedBlock--;
                    _loaded.InsertRange(0, block);
                    _exposedStart += block.Length;
                    _exposedEnd += block.Length;
                }
                var take = Math.Min(count - revealed.Count, _exposedStart);
                revealed.InsertRange(0, _loaded.GetRange(_exposedStart - take, take));
                _exposedStart -= take;
            }
            return new GalleryPage(revealed, HasNewer);
        }
        finally {
            _lock.Release();
        }
    }

    public async Task<GalleryPage> LoadOlder(int count, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var revealed = new List<VisualMediaItem>();
            while (revealed.Count < count) {
                if (_exposedEnd >= _loaded.Count) {
                    if (!await TryLoadOlderBlock(cancellationToken).ConfigureAwait(false))
                        break;
                }
                var take = Math.Min(count - revealed.Count, _loaded.Count - _exposedEnd);
                revealed.AddRange(_loaded.GetRange(_exposedEnd, take));
                _exposedEnd += take;
            }
            return new GalleryPage(revealed, HasOlder);
        }
        finally {
            _lock.Release();
        }
    }

    public async Task<ChatEntryAttachment?> ResolveAttachment(VisualMediaItem item, CancellationToken cancellationToken)
    {
        if (!_entryCache.TryGetValue(item.EntryId, out var entry)) {
            entry = await Chats.GetEntry(session, item.EntryId, cancellationToken).ConfigureAwait(false);
            _entryCache[item.EntryId] = entry;
        }
        return entry?.Attachments.FirstOrDefault(a => a.Index == item.LocalIndex);
    }

    // Private methods

    private async Task EnsureSkeletonHead(CancellationToken cancellationToken)
    {
        if (_skeletonStarted)
            return;

        do {
            await PullNextSkeletonPage(cancellationToken).ConfigureAwait(false);
        } while (_periods.Count == 0 && _cursor != null);
        _blocks = ContentListPlumbing.BuildBlocks(_periods);
    }

    private async Task<bool> PullNextSkeletonPage(CancellationToken cancellationToken)
    {
        var page = await Chats
            .GetContentPeriods(session, chatId, ChatContentKind.Media, _cursor, cancellationToken)
            .ConfigureAwait(false);
        _periods.AddRange(page.Periods);
        _cursor = page.NextPeriodKey;
        _skeletonStarted = true;
        return page.Periods.Length > 0;
    }

    private async Task<VisualMediaItem[]> EnsureBlockLoaded(int index, CancellationToken cancellationToken)
    {
        if (_blockItems.TryGetValue(index, out var cached))
            return cached;

        var block = _blocks[index];
        var items = await Chats
            .GetVisualMediaPeriod(session, chatId, block.PeriodKey, block.PageIndex, cancellationToken)
            .ConfigureAwait(false);
        // Backend returns a page oldest-first; the flat sequence is newest-first.
        // GetVisualMediaPeriod is a [ComputeMethod] — its array is cached and shared,
        // so reverse into a fresh array instead of mutating it in place.
        var newestFirst = items.Reverse().ToArray();
        _blockItems[index] = newestFirst;
        return newestFirst;
    }

    private async Task<bool> TryLoadOlderBlock(CancellationToken cancellationToken)
    {
        while (_lastLoadedBlock >= _blocks.Count - 1) {
            if (_cursor == null)
                return false;

            await PullNextSkeletonPage(cancellationToken).ConfigureAwait(false);
            _blocks = ContentListPlumbing.BuildBlocks(_periods);
        }
        var next = _lastLoadedBlock + 1;
        var block = await EnsureBlockLoaded(next, cancellationToken).ConfigureAwait(false);
        _lastLoadedBlock = next;
        _loaded.AddRange(block);
        return true;
    }

    private async Task<(int Block, int Pos)> LocateAnchor(VisualMediaItem anchor, CancellationToken cancellationToken)
    {
        if (_blocks.Count == 0)
            return (-1, -1);

        var at = anchor.At.ToDateTime();
        var monthKey = $"{at.Year:D4}-{at.Month:D2}";
        while (true) {
            var idx = _blocks.FindIndex(b => b.PeriodKey == monthKey);
            if (idx >= 0) {
                var first = idx;
                while (first > 0 && _blocks[first - 1].PeriodKey == monthKey)
                    first--;
                var last = idx;
                while (last < _blocks.Count - 1 && _blocks[last + 1].PeriodKey == monthKey)
                    last++;
                for (var b = first; b <= last; b++) {
                    var items = await EnsureBlockLoaded(b, cancellationToken).ConfigureAwait(false);
                    var pos = Array.FindIndex(items, x => x.Id == anchor.Id);
                    if (pos >= 0)
                        return (b, pos);
                }
                break;
            }
            if (_cursor == null)
                break;

            await PullNextSkeletonPage(cancellationToken).ConfigureAwait(false);
            _blocks = ContentListPlumbing.BuildBlocks(_periods);
        }
        return await LinearLocate(anchor, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(int Block, int Pos)> LinearLocate(VisualMediaItem anchor, CancellationToken cancellationToken)
    {
        for (var b = 0;; b++) {
            while (b >= _blocks.Count) {
                if (_cursor == null)
                    return (-1, -1);

                await PullNextSkeletonPage(cancellationToken).ConfigureAwait(false);
                _blocks = ContentListPlumbing.BuildBlocks(_periods);
            }
            var items = await EnsureBlockLoaded(b, cancellationToken).ConfigureAwait(false);
            var pos = Array.FindIndex(items, x => x.Id == anchor.Id);
            if (pos >= 0)
                return (b, pos);
        }
    }
}
