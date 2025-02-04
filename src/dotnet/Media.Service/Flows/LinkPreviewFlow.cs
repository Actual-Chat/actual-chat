using ActualChat.Flows;
using ActualChat.Media.Module;
using MemoryPack;

namespace ActualChat.Media.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LinkPreviewFlow : Flow
{
    [field: AllowNull, MaybeNull]
    private MediaSettings Settings => field ??= Host.Services.GetRequiredService<MediaSettings>();
    [field: AllowNull, MaybeNull]
    private ILinkPreviewsBackend LinkPreviewsBackend => field ??= Host.Services.GetRequiredService<ILinkPreviewsBackend>();
    [field: AllowNull, MaybeNull]
    private Crawler Crawler => field ??= Host.Services.GetRequiredService<Crawler>();

    public static string BuildArgs(string url)
        => url.ToBase64();

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        await Run(cancellationToken).ConfigureAwait(false);
        return WaitForEvent(nameof(OnReset), Settings.LinkPreviewUpdatePeriod);
    }

    private async Task Run(CancellationToken cancellationToken)
    {
        var url = Id.Arguments.FromBase64();
        var id = LinkPreview.ComposeId(url);

        var linkPreview = await LinkPreviewsBackend.Get(id, false, cancellationToken).ConfigureAwait(false);
        if (linkPreview != null && !NeedsUpdate(linkPreview.ModifiedAt))
            return;

        var linkMeta = await Crawler.Crawl(url, cancellationToken).ConfigureAwait(false);
        var videoMeta = linkMeta.OpenGraph.Video;
        linkPreview ??= new LinkPreview {
            Id = LinkPreview.ComposeId(url),
            Url = url,
            PreviewMediaId = linkMeta.PreviewMediaId,
        };
        if (linkMeta.OpenGraph != OpenGraph.None)
            linkPreview = linkPreview with {
                Title = linkMeta.OpenGraph.Title,
                Description = linkMeta.OpenGraph.Description,
            };
        if (videoMeta != OpenGraphVideo.None)
            linkPreview = linkPreview with {
                VideoSite = linkMeta.OpenGraph.SiteName,
                VideoUrl = linkMeta.OpenGraph.Video.SecureUrl,
                VideoWidth = linkMeta.OpenGraph.Video.Width,
                VideoHeight = linkMeta.OpenGraph.Video.Height,
            };
        var cmd = new LinkPreviewsBackend_Change(id, null, Change.Upsert(linkPreview));
        await Host.Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }

    private bool NeedsUpdate(Moment modifiedAt)
        => modifiedAt + Settings.LinkPreviewUpdatePeriod < Host.Clocks.SystemClock.Now;
}
