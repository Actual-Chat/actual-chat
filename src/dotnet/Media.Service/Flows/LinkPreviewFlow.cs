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
    private GithubLinkCards GithubLinkCards => field ??= Services.GetRequiredService<GithubLinkCards>();
    private ImageGrabber ImageGrabber => field ??= Services.GetRequiredService<ImageGrabber>();
    private ILogger Log => field ??= Services.LogFor(GetType());
    private ICommander Commander => field ??= Services.Commander();

    protected override TimeSpan ThrottlePeriod => MediaSettings.LinkPreviewUpdatePeriod;

    protected override async ValueTask Run(CancellationToken cancellationToken)
    {
        var id = LinkPreview.ComposeId(Target);
        var linkPreview = await LinkPreviewsBackend.Get(id, false, cancellationToken).ConfigureAwait(false);

        var githubLink = GithubLinkParser.TryParse(Target) is { } githubTarget
            ? await GithubLinkCards.Get(githubTarget, cancellationToken).ConfigureAwait(false)
            : null;
        if (githubLink is not null) {
            linkPreview ??= new LinkPreview { Id = id, Url = Target };
            linkPreview = linkPreview with {
                Title = githubLink.Title,
                Description = githubLink.Description,
                PreviewMediaId = await GrabAvatar(githubLink.AvatarUrl, cancellationToken).ConfigureAwait(false),
                GithubCard = githubLink.Card,
            };
        }
        else
            linkPreview = await Crawl(linkPreview, cancellationToken).ConfigureAwait(false);
        var cmd = new LinkPreviewsBackend_Change(id, null, Change.Upsert(linkPreview));
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<MediaId?> GrabAvatar(string avatarUrl, CancellationToken cancellationToken)
    {
        if (avatarUrl.IsNullOrEmpty())
            return null;

        try {
            return await ImageGrabber.GetOrGrab(avatarUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to grab the avatar '{AvatarUrl}' for '{Url}'", avatarUrl, Target);
            return null;
        }
    }

    private async Task<LinkPreview> Crawl(LinkPreview? linkPreview, CancellationToken cancellationToken)
    {
        using var activity = CoreServerInstruments.ActivitySource
            .StartActivity(typeof(Crawler), nameof(Crawler.Crawl), ActivityKind.Client);
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
        // A short map link has no coordinates in it, but the URL it redirects to usually does
        if ((MapLinkParser.TryParse(Target) ?? MapLinkParser.TryParse(linkMeta.ResolvedUrl)) is { } mapLinkTarget)
            linkPreview = linkPreview with { MapPoint = mapLinkTarget.Point, MapPointName = mapLinkTarget.Name };
        return linkPreview;
    }
}
