using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;

namespace ActualChat.UI.Blazor.App.Components;

// Navigable media-viewer collection over a chat's whole visual-media library.
// Reuses the period-skeleton + paged-page protocol (and the block helpers) that the
// right-panel grid uses, yields a flat newest-first window (index 0 = newest), and
// extends it at either edge on demand. Items are synthetic ChatEntryAttachments built
// from VisualMediaItem; Width/Height are filled lazily via GetEntry in EnsureResolved.
public sealed class MediaIndexCollectionView : IMediaCollectionView
{
    private const int Batch = 10;
    private const int ResolveRadius = 2;

    private readonly AppUIHub _hub;
    private readonly Session _session;
    private readonly ChatId _chatId;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<ChatContentPeriod> _periods = new();
    private readonly Dictionary<int, VisualMediaItem[]> _blockItems = new();
    private readonly List<VisualMediaItem> _loaded = new();
    private readonly List<ChatEntryAttachment> _items = new();
    private readonly HashSet<Symbol> _resolved = new();
    private List<ContentListPlumbing.Block> _blocks = new();
    private string? _cursor;
    private bool _skeletonStarted;
    private int _firstLoadedBlock = -1;
    private int _lastLoadedBlock = -1;
    private int _exposedStart;
    private int _exposedEnd;

    public IReadOnlyList<ChatEntryAttachment> Items => _items;
    public int InitialIndex { get; private set; }
    public bool HasNewer => _exposedStart > 0 || _firstLoadedBlock > 0;
    public bool HasOlder => _exposedEnd < _loaded.Count || _lastLoadedBlock < _blocks.Count - 1 || _cursor != null;

    private IChats Chats => _hub.Chats;

    private MediaIndexCollectionView(AppUIHub hub, Session session, ChatId chatId)
    {
        _hub = hub;
        _session = session;
        _chatId = chatId;
    }

    public static async Task<MediaIndexCollectionView> Create(
        AppUIHub hub,
        Session session,
        ChatId chatId,
        VisualMediaItem anchor,
        int radius,
        CancellationToken cancellationToken)
    {
        var view = new MediaIndexCollectionView(hub, session, chatId);
        await view.Initialize(anchor, radius, cancellationToken).ConfigureAwait(false);
        return view;
    }

    public async Task<int> LoadNewer(CancellationToken cancellationToken)
    {
        var items = await LoadNewerItems(Batch, cancellationToken).ConfigureAwait(false);
        for (var i = items.Count - 1; i >= 0; i--)
            _items.Insert(0, ToSyntheticAttachment(items[i]));
        return items.Count;
    }

    public async Task<int> LoadOlder(CancellationToken cancellationToken)
    {
        var items = await LoadOlderItems(Batch, cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
            _items.Add(ToSyntheticAttachment(item));
        return items.Count;
    }

    public async ValueTask EnsureResolved(int index, CancellationToken cancellationToken)
    {
        var from = Math.Max(0, index - ResolveRadius);
        var to = Math.Min(_items.Count - 1, index + ResolveRadius);
        for (var i = from; i <= to; i++) {
            var synthetic = _items[i];
            if (!_resolved.Add(synthetic.Id))
                continue;

            var real = await ResolveReal(synthetic, cancellationToken).ConfigureAwait(false);
            if (real != null && i < _items.Count && ReferenceEquals(_items[i], synthetic))
                _items[i] = real;
        }
    }

    // Private methods

    private async Task Initialize(VisualMediaItem anchor, int radius, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await EnsureSkeletonHead(cancellationToken).ConfigureAwait(false);
            var (anchorBlock, anchorPos) = await LocateAnchor(anchor, cancellationToken).ConfigureAwait(false);
            if (anchorBlock < 0) {
                _items.Add(ToSyntheticAttachment(anchor));
                InitialIndex = 0;
                return;
            }

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
            for (var i = _exposedStart; i < _exposedEnd; i++)
                _items.Add(ToSyntheticAttachment(_loaded[i]));
            InitialIndex = anchorIndex - _exposedStart;
        }
        finally {
            _lock.Release();
        }
    }

    private async Task<List<VisualMediaItem>> LoadNewerItems(int count, CancellationToken cancellationToken)
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
            return revealed;
        }
        finally {
            _lock.Release();
        }
    }

    private async Task<List<VisualMediaItem>> LoadOlderItems(int count, CancellationToken cancellationToken)
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
            return revealed;
        }
        finally {
            _lock.Release();
        }
    }

    private async Task<ChatEntryAttachment?> ResolveReal(ChatEntryAttachment synthetic, CancellationToken cancellationToken)
    {
        var entry = await Chats.GetEntry(_session, synthetic.EntryId, cancellationToken).ConfigureAwait(false);
        return entry?.Attachments.FirstOrDefault(a => a.Index == synthetic.Index);
    }

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
            .GetContentPeriods(_session, _chatId, ChatContentKind.Media, _cursor, cancellationToken)
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
            .GetVisualMediaPeriod(_session, _chatId, block.PeriodKey, block.PageIndex, cancellationToken)
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

    private static ChatEntryAttachment ToSyntheticAttachment(VisualMediaItem item)
    {
        var media = new Media.Media(item.MediaId) {
            BlobId = item.BlobId,
            ContentType = item.ContentType,
            FileName = item.FileName,
            Length = item.Size,
        };
        Media.Media? thumbnailMedia = null;
        if (item.ThumbnailMediaId is { } thumbnailMediaId && !item.ThumbnailBlobId.IsNullOrEmpty())
            thumbnailMedia = new Media.Media(thumbnailMediaId) { BlobId = item.ThumbnailBlobId };
        return new ChatEntryAttachment(item.Id) {
            EntryId = item.EntryId,
            Index = item.LocalIndex,
            MediaId = item.MediaId,
            Media = media,
            ThumbnailMediaId = item.ThumbnailMediaId,
            ThumbnailMedia = thumbnailMedia,
        };
    }
}
