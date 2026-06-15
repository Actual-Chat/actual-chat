using ActualChat.Chat;

namespace ActualChat.UI.Blazor.Components;

// What VisualMediaViewerModal pages through: a windowed, newest-first list of
// attachments (index 0 = newest). Fixed sets (a message's attachments) and the
// navigable chat media library both implement this — the viewer never branches.
public interface IMediaCollectionView
{
    IReadOnlyList<ChatEntryAttachment> Items { get; }
    int InitialIndex { get; }
    bool HasNewer { get; }
    bool HasOlder { get; }
    // Extend the window; return how many items were added at that edge (0 = none).
    Task<int> LoadNewer(CancellationToken cancellationToken);
    Task<int> LoadOlder(CancellationToken cancellationToken);
    // Upgrade lazily-loaded items around index (no-op when Items are already complete).
    ValueTask EnsureResolved(int index, CancellationToken cancellationToken);
}

// A closed attachment set (e.g. one chat entry's attachments) — no navigation.
public sealed class FixedMediaCollectionView(IReadOnlyList<ChatEntryAttachment> items, int initialIndex)
    : IMediaCollectionView
{
    public IReadOnlyList<ChatEntryAttachment> Items { get; } = items;
    public int InitialIndex { get; } = initialIndex;
    public bool HasNewer => false;
    public bool HasOlder => false;
    public Task<int> LoadNewer(CancellationToken cancellationToken) => Task.FromResult(0);
    public Task<int> LoadOlder(CancellationToken cancellationToken) => Task.FromResult(0);
    public ValueTask EnsureResolved(int index, CancellationToken cancellationToken) => default;
}
