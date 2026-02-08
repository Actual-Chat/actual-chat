using ActualChat.Diagnostics;
using ActualChat.Flows;
using ActualChat.Media.Module;
using ActualLab.Diagnostics;

namespace ActualChat.Media.Flows;

[Flow(DelayQuanta = 1)] // Extra resumes are fine
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class LinkPreviewFlow : ThrottledUpdateFlow
{
    private MediaSettings MediaSettings => field ??= Services.GetRequiredService<MediaSettings>();
    private ILinkPreviewsBackend LinkPreviewsBackend => field ??= Services.GetRequiredService<ILinkPreviewsBackend>();
    private Crawler Crawler => field ??= Services.GetRequiredService<Crawler>();
    private ICommander Commander => field ??= Services.Commander();

    protected override TimeSpan ThrottlePeriod => MediaSettings.LinkPreviewUpdatePeriod;

    protected override async ValueTask Run(CancellationToken cancellationToken)
    {
        var id = LinkPreview.ComposeId(Target);
        var linkPreview = await LinkPreviewsBackend.Get(id, false, cancellationToken).ConfigureAwait(false);

        using var activity = CoreServerInstruments.ActivitySource.StartActivity(typeof(Crawler), nameof(Crawler.Crawl), ActivityKind.Client);
        var linkMeta = await Crawler.Crawl(Target, cancellationToken)
            .WithActivity(activity, cancellationToken)
            .ConfigureAwait(false);
        var videoMeta = linkMeta.OpenGraph.Video;
        linkPreview ??= new LinkPreview {
            Id = LinkPreview.ComposeId(Target),
            Url = Target,
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
}
