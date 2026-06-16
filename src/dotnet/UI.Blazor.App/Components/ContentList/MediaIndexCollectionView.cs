using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Components;

// Navigable media-viewer collection over a chat's whole visual-media library.
// Reuses the period-skeleton + paged-page protocol (and the block helpers) that the
// right-panel grid uses, yields a flat newest-first window (index 0 = newest), and
// extends it at either edge on demand while trimming the far edge to cap the window at
// MaxWindow (the loaded buffer is kept, so trimmed items re-reveal without a refetch).
// Load* return the signed shift of existing items' indices (prepend +N, far-edge trim -N).
// Items are synthetic ChatEntryAttachments built from VisualMediaItem; Width/Height are
// filled lazily via GetEntry in EnsureResolved.
public sealed class MediaIndexCollectionView : IMediaCollectionView
{
    private const int Batch = 10;
    // Must exceed Batch + 2*viewer LoadThreshold so that after a far-edge trim the active
    // slide can't land back inside the opposite edge's load zone (which would thrash loads).
    private const int MaxWindow = 40;
    private const int ResolveRadius = 2;

    private readonly AppUIHub _hub;
    private readonly Session _session;
    private readonly ChatId _chatId;
    private readonly AsyncLock _lock = new();
    private readonly List<ChatContentPeriod> _periods = new();
    private readonly Dictionary<int, VisualMediaItem[]> _blockItems = new();
    private readonly List<VisualMediaItem> _loaded = new();
    private readonly List<ChatEntryAttachment> _items = new();
    private readonly HashSet<Symbol> _resolved = new();
    private List<ContentListPlumbing.Block> _blocks = new();
    private string? _cursor;
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
        string anchorRowKey,
        int radius,
        CancellationToken cancellationToken)
    {
        var view = new MediaIndexCollectionView(hub, session, chatId);
        await view.Initialize(anchor, anchorRowKey, radius, cancellationToken).ConfigureAwait(false);
        return view;
    }

    public async Task<int> LoadNewer(CancellationToken cancellationToken)
    {
        var items = await LoadNewerItems(Batch, cancellationToken).ConfigureAwait(false);
        for (var i = items.Count - 1; i >= 0; i--)
            _items.Insert(0, ToSyntheticAttachment(items[i]));
        TrimBack();
        return items.Count;
    }

    public async Task<int> LoadOlder(CancellationToken cancellationToken)
    {
        var items = await LoadOlderItems(Batch, cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
            _items.Add(ToSyntheticAttachment(item));
        return -TrimFront();
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

    private async Task Initialize(VisualMediaItem anchor, string anchorRowKey, int radius, CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        var (anchorBlock, anchorPos) = await LocateAnchor(anchor, anchorRowKey, cancellationToken).ConfigureAwait(false);
        if (anchorBlock < 0) {
            _items.Add(ToSyntheticAttachment(anchor));
            InitialIndex = 0;
            return;
        }

        _firstLoadedBlock = _lastLoadedBlock = anchorBlock;
        _loaded.Clear();
        _loaded.AddRange(_blockItems[anchorBlock]);
        _exposedStart = anchorPos;
        _exposedEnd = anchorPos + 1;
        _items.Add(ToSyntheticAttachment(_loaded[anchorPos]));

        var newer = await RevealNewer(radius, cancellationToken).ConfigureAwait(false);
        for (var i = newer.Count - 1; i >= 0; i--)
            _items.Insert(0, ToSyntheticAttachment(newer[i]));
        foreach (var item in await RevealOlder(radius, cancellationToken).ConfigureAwait(false))
            _items.Add(ToSyntheticAttachment(item));
        InitialIndex = newer.Count;
    }

    private async Task<List<VisualMediaItem>> LoadNewerItems(int count, CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        return await RevealNewer(count, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<VisualMediaItem>> LoadOlderItems(int count, CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        return await RevealOlder(count, cancellationToken).ConfigureAwait(false);
    }

    // Reveal cores — caller must hold _lock. Move the exposed window's edge outward by up to
    // `count`, loading more blocks as needed, and return the newly exposed items.
    private async Task<List<VisualMediaItem>> RevealNewer(int count, CancellationToken cancellationToken)
    {
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

    private async Task<List<VisualMediaItem>> RevealOlder(int count, CancellationToken cancellationToken)
    {
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

    private async Task<ChatEntryAttachment?> ResolveReal(ChatEntryAttachment synthetic, CancellationToken cancellationToken)
    {
        var entry = await Chats.GetEntry(_session, synthetic.EntryId, cancellationToken).ConfigureAwait(false);
        return entry?.Attachments.FirstOrDefault(a => a.Index == synthetic.Index);
    }

    // Trims the newer (front) edge back to MaxWindow and returns how many items were dropped;
    // _loaded keeps them, so LoadNewer re-reveals them later without a refetch.
    private int TrimFront()
    {
        var excess = _items.Count - MaxWindow;
        if (excess <= 0)
            return 0;

        for (var i = 0; i < excess; i++)
            _resolved.Remove(_items[i].Id);
        _items.RemoveRange(0, excess);
        _exposedStart += excess;
        return excess;
    }

    // Trims the older (back) edge back to MaxWindow; back-edge drops don't shift existing indices.
    private void TrimBack()
    {
        var excess = _items.Count - MaxWindow;
        if (excess <= 0)
            return;

        var start = _items.Count - excess;
        for (var i = start; i < _items.Count; i++)
            _resolved.Remove(_items[i].Id);
        _items.RemoveRange(start, excess);
        _exposedEnd -= excess;
    }

    private async Task PullNextSkeletonPage(CancellationToken cancellationToken)
    {
        var page = await Chats
            .GetContentPeriods(_session, _chatId, ChatContentKind.Media, _cursor, cancellationToken)
            .ConfigureAwait(false);
        _periods.AddRange(page.Periods);
        _cursor = page.NextPeriodKey;
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

    // The grid row the anchor was clicked in carries an opaque "r|i:PeriodKey:PageIndex:row"
    // key; FindBlockIndex recovers its block without parsing the PeriodKey format. Page the
    // skeleton until that block surfaces, then locate the anchor within it by Id. Returns
    // (-1, -1) if the skeleton is exhausted without a match, or the anchor drifted off its
    // page since the grid rendered it — the caller then shows the clicked item on its own.
    private async Task<(int Block, int Pos)> LocateAnchor(VisualMediaItem anchor, string anchorRowKey, CancellationToken cancellationToken)
    {
        while (true) {
            await PullNextSkeletonPage(cancellationToken).ConfigureAwait(false);
            _blocks = ContentListPlumbing.BuildBlocks(_periods);

            var idx = ContentListPlumbing.FindBlockIndex(_blocks, anchorRowKey);
            if (idx >= 0) {
                var items = await EnsureBlockLoaded(idx, cancellationToken).ConfigureAwait(false);
                var pos = Array.FindIndex(items, x => x.Id == anchor.Id);
                return pos >= 0 ? (idx, pos) : (-1, -1);
            }
            if (_cursor == null)
                return (-1, -1);
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
