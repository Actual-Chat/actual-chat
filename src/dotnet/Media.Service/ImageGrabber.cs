using System.Text;
using ActualChat.Flows;
using ActualChat.Hashing;
using ActualChat.Media.Db;
using ActualChat.Media.Flows;
using ActualChat.Media.Module;
using ActualChat.Mesh;
using ActualChat.Uploads;
using ActualLab.IO;

namespace ActualChat.Media;

public class ImageGrabber(IServiceProvider services)
{
    [field: AllowNull, MaybeNull]
    private MediaSettings Settings => field ??= services.GetRequiredService<MediaSettings>();
    [field: AllowNull, MaybeNull]
    private IMediaBackend MediaBackend => field ??= services.GetRequiredService<IMediaBackend>();
    [field: AllowNull, MaybeNull]
    private IGrabStatusesBackend GrabStatusesBackend => field ??= services.GetRequiredService<IGrabStatusesBackend>();
    [field: AllowNull, MaybeNull]
    private IContentSaver ContentSaver => field ??= services.GetRequiredService<IContentSaver>();
    [field: AllowNull, MaybeNull]
    private HttpClient HttpClient => field ??= services.HttpClientFactory().CreateClient(Crawler.HttpClientName);
    [field: AllowNull, MaybeNull]
    private IReadOnlyList<IUploadProcessor> UploadProcessors => field ??= services.GetServices<IUploadProcessor>().ToList();
    [field: AllowNull, MaybeNull]
    private IFlows Flows => field ??= services.GetRequiredService<IFlows>();
    [field: AllowNull, MaybeNull]
    private IMeshLocks MeshLocks => field ??= services.MeshLocks<MediaDbContext>().WithKeyPrefix(nameof(ImageGrabber));
    [field: AllowNull, MaybeNull]
    private ICommander Commander => field ??= services.Commander();
    [field: AllowNull, MaybeNull]
    private MomentClockSet Clocks => field ??= services.Clocks();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services.LogFor(GetType());

    public async Task<MediaId> GetOrGrab(string imageUrl, CancellationToken cancellationToken)
    {
        if (imageUrl.IsNullOrEmpty())
            return MediaId.None;

        var existingId = await GetExisting();
        if (existingId != MediaId.None) {
            await ScheduleUpdateIfRequired(imageUrl, cancellationToken).ConfigureAwait(false);
            return existingId;
        }

        var (_, mediaId) = await MeshLocks.RunLocked(imageUrl.Hash().SHA256().AlphaNumeric(),
            RunLockedOptions.Default,
            async ct => {
                existingId = await GetExisting();
                if (existingId != MediaId.None)
                    return existingId;

                return await GrabUnsafe(imageUrl, ct);
            },
            cancellationToken);
        return mediaId;

        async Task<MediaId> GetExisting()
        {
            var existingMedia = await MediaBackend.GetByMediaIdScope(GetMediaIdScope(imageUrl), cancellationToken);
            return existingMedia?.Id ?? MediaId.None;
        }
    }

    public async Task UpdateExisting(string imageUrl, CancellationToken cancellationToken)
    {
        try {
            // No mesh locks because it's called from flow which is always not concurrent
            await GrabUnsafe(imageUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to update existing link preview thumbnail");
            await SaveGrabStatus(imageUrl, false, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<MediaId> GrabUnsafe(string imageUrl, CancellationToken cancellationToken)
    {
        var grabStatus = await GrabStatusesBackend.GetByUrl(imageUrl, cancellationToken).ConfigureAwait(false);
        if (!NeedsUpdate(grabStatus)) {
            var media = await MediaBackend.GetByMediaIdScope(GetMediaIdScope(imageUrl), cancellationToken).ConfigureAwait(false);
            if (media != null)
                return media.Id;
        }

        // TODO: image size limit
        var processedFile = await DownloadImageToFile(imageUrl, cancellationToken).ConfigureAwait(false);
        return await SaveFileToMedia(imageUrl, processedFile, cancellationToken).ConfigureAwait(false);
    }

    private async Task ScheduleUpdateIfRequired(string imageUrl, CancellationToken cancellationToken)
    {
        var grabStatus = await GrabStatusesBackend.GetByUrl(imageUrl, cancellationToken).ConfigureAwait(false);
        if (!NeedsUpdate(grabStatus))
            return;

        await Flows.StartOrReset<PreviewThumbnailUpdateFlow>(
                PreviewThumbnailUpdateFlow.BuildArgs(imageUrl),
                GetUpdatePeriod(grabStatus),
                "Schedule update",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProcessedFile?> DownloadImageToFile(string imageUrl, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(Settings.ImageDownloadTimeout);
        return await Download(cts.Token).ConfigureAwait(false);

        async Task<ProcessedFile?> Download(CancellationToken cancellationToken1)
        {
            HttpResponseMessage response;
            try {
                response = await HttpClient.GetAsync(imageUrl, cancellationToken1).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to get an image with url='{ImageUrl}'", imageUrl);
                return null;
            }

            return await SaveImageToFile(response, cancellationToken1).ConfigureAwait(false);
        }
    }

    private async Task<ProcessedFile?> SaveImageToFile(HttpResponseMessage response, CancellationToken cancellationToken1)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var ext = MediaTypeExt.GetFileExtension(contentType); // TODO: convert if icon is not supported
        if (ext.IsNullOrEmpty())
            return null;

        var lastSegment = response.RequestMessage!.RequestUri!.Segments[^1].TrimSuffix(ext);
        FilePath fileName = new string(lastSegment.Where(Alphabet.AlphaNumeric.IsMatch).ToArray()) + ext;
        var file = new UploadedStreamFile(fileName, contentType, response.Content.Headers.ContentLength ?? 0, () => response.Content.ReadAsStreamAsync(cancellationToken1));
        return await UploadProcessors.Process(file, cancellationToken1).ConfigureAwait(false);
    }

    private async Task<MediaId> SaveFileToMedia(
        string imageUrl,
        ProcessedFile? processedFile,
        CancellationToken cancellationToken)
    {
        if (processedFile is null || !MediaTypeExt.IsSupportedImage(processedFile.File.ContentType))
            return MediaId.None;

        var mediaIdScope = GetMediaIdScope(imageUrl);
        var mediaLid = await processedFile.File.Process(async stream => {
            var hash = await stream.Hash().SHA256(cancellationToken).ConfigureAwait(false);
            return hash.AlphaNumeric();
        }).ConfigureAwait(false);
        var mediaId = new MediaId(mediaIdScope, mediaLid);
        var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (media is not null) {
            await SaveGrabStatus(imageUrl, true, cancellationToken).ConfigureAwait(false);
            return media.Id;
        }

        // TODO: extract common part with ChatMediaController
        media = new Media(mediaId) {
            ContentId = mediaId.ContentId(processedFile.File.FileName.Extension),
            FileName = processedFile.File.FileName,
            Length = processedFile.File.Length,
            ContentType = processedFile.File.ContentType,
            Width = processedFile.Size?.Width ?? 0,
            Height = processedFile.Size?.Height ?? 0,
        };

        var stream = await processedFile.File.Open().ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        var content = new Content(media.ContentId, media.ContentType, stream);
        await ContentSaver.Save(content, cancellationToken).ConfigureAwait(false);

        var changeCommand = new MediaBackend_Change(
            mediaId,
            new Change<Media> {
                Create = media,
            });
        await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        await SaveGrabStatus(imageUrl, true, cancellationToken).ConfigureAwait(false);

        return mediaId;
    }

    private static string GetMediaIdScope(string imageUrl)
        => imageUrl.Hash(Encoding.UTF8).SHA256().AlphaNumeric();

    private Task<GrabStatus> SaveGrabStatus(string imageUrl, bool success, CancellationToken cancellationToken)
        => Commander.Call(new GrabStatusesBackend_Change(GrabStatus.ComposeId(imageUrl), success),
            true,
            cancellationToken);

    private bool NeedsUpdate(GrabStatus? grabStatus) => grabStatus is null
        || grabStatus.ModifiedAt + GetUpdatePeriod(grabStatus) < Clocks.SystemClock.Now;

    private TimeSpan GetUpdatePeriod(GrabStatus? grabStatus)
        => grabStatus?.Success != false ? Settings.LinkPreviewUpdatePeriod : TimeSpan.FromHours(1);
}
