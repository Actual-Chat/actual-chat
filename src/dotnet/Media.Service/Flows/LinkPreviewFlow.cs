using ActualChat.Diagnostics;
using ActualChat.Flows;
using ActualChat.Media.Module;
using ActualLab.Diagnostics;
using MemoryPack;

namespace ActualChat.Media.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LinkPreviewFlow : PeriodicFlow
{
    private MediaSettings Settings => field ??= Services.GetRequiredService<MediaSettings>();
    private ILinkPreviewsBackend LinkPreviewsBackend => field ??= Services.GetRequiredService<ILinkPreviewsBackend>();
    private Crawler Crawler => field ??= Services.GetRequiredService<Crawler>();
    private ICommander Commander => field ??= Services.Commander();

    public static string GetArguments(string url)
        => url.ToBase64();

    protected override async Task Run(CancellationToken cancellationToken)
    {
        var url = Id.Arguments.FromBase64();
        var id = LinkPreview.ComposeId(url);

        var linkPreview = await LinkPreviewsBackend.Get(id, false, cancellationToken).ConfigureAwait(false);
        if (linkPreview != null && !NeedsUpdate(linkPreview.ModifiedAt))
            return;

        using var activity = CoreServerInstruments.ActivitySource.StartActivity(typeof(Crawler), nameof(Crawler.Crawl), ActivityKind.Client);
        var linkMeta = await Crawler.Crawl(url, cancellationToken)
            .WithActivity(activity, cancellationToken)
            .ConfigureAwait(false);
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
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }

    protected override ValueTask<Moment> GetNextRunAt(CancellationToken cancellationToken)
        => new(Hub.SystemNow + Settings.LinkPreviewUpdatePeriod);

    private bool NeedsUpdate(Moment modifiedAt)
        => modifiedAt + Settings.LinkPreviewUpdatePeriod < Hub.SystemNow;
}
