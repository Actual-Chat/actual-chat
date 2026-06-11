using ActualChat.Chat;

namespace ActualChat.UI.Blazor.Components;

// Lets VisualMediaViewerModal page through a whole chat's media library instead of a
// fixed attachment set. The Window is a newest-first slice (index 0 = newest); callbacks
// extend it at either edge and resolve full attachments (with Media.Width/Height) lazily.
public sealed record GalleryContext(
    IReadOnlyList<VisualMediaItem> Window,
    int AnchorIndex,
    bool HasNewer,
    bool HasOlder,
    Func<int, CancellationToken, Task<GalleryPage>> LoadNewer,
    Func<int, CancellationToken, Task<GalleryPage>> LoadOlder,
    Func<VisualMediaItem, CancellationToken, Task<ChatEntryAttachment?>> ResolveAttachment);

public sealed record GalleryInit(
    IReadOnlyList<VisualMediaItem> Window,
    int AnchorIndex,
    bool HasNewer,
    bool HasOlder);

public sealed record GalleryPage(IReadOnlyList<VisualMediaItem> Items, bool HasMore);
