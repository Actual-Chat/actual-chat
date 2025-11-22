using System.Net.Http.Headers;
using System.Text;
using ActualChat.Flows;
using ActualChat.Hashing;
using ActualChat.Media.Db;
using ActualChat.Media.Flows;
using ActualChat.Media.Module;
using ActualChat.Uploads;
using ActualLab.IO;

namespace ActualChat.Media;

public class ImageGrabber(IServiceProvider services)
{
    private MediaSettings Settings => field ??= services.GetRequiredService<MediaSettings>();
    private IMediaBackend MediaBackend => field ??= services.GetRequiredService<IMediaBackend>();
    private IGrabStatusesBackend GrabStatusesBackend => field ??= services.GetRequiredService<IGrabStatusesBackend>();
    private IMediaSaver MediaSaver { get; } = services.GetRequiredService<IMediaSaver>();
    private HttpClient HttpClient => field ??= services.HttpClientFactory().CreateClient(Crawler.HttpClientName);
    private IReadOnlyList<IUploadProcessor> UploadProcessors => field ??= services.GetServices<IUploadProcessor>().ToList();
    private IFlows Flows => field ??= services.GetRequiredService<IFlows>();
    private IMeshLocks MeshLocks => field ??= services.MeshLocks<MediaDbContext>().WithKeyPrefix(nameof(ImageGrabber));
    private ICommander Commander => field ??= services.Commander();
    private MomentClockSet Clocks => field ??= services.Clocks();
    private ILogger Log => field ??= services.LogFor(GetType());

    public async Task<MediaId?> GetOrGrab(string imageUrl, CancellationToken cancellationToken)
    {
        if (imageUrl.IsNullOrEmpty())
            return null;

        var existingId = await GetExisting().ConfigureAwait(false);
        if (existingId != null) {
            await ScheduleUpdateIfRequired(imageUrl, cancellationToken).ConfigureAwait(false);
            return existingId;
        }

        var mediaId = await MeshLocks.LockAndRun(
            imageUrl.Hash().SHA256().AlphaNumeric(),
            async ct => {
                existingId = await GetExisting().ConfigureAwait(false);
                return existingId ?? await GrabUnsafe(imageUrl, ct).ConfigureAwait(false);
            },
            cancellationToken
            ).ConfigureAwait(false);
        return mediaId;

        async Task<MediaId?> GetExisting() {
            var existingMedia = await MediaBackend
                .GetByMediaIdScope(GetMediaIdScope(imageUrl), cancellationToken)
                .ConfigureAwait(false);
            return existingMedia?.Id;
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

    private async Task<MediaId?> GrabUnsafe(string imageUrl, CancellationToken cancellationToken)
    {
        var grabStatus = await GrabStatusesBackend.GetByUrl(imageUrl, cancellationToken).ConfigureAwait(false);
        if (!NeedsUpdate(grabStatus)) {
            var media = await MediaBackend.GetByMediaIdScope(GetMediaIdScope(imageUrl), cancellationToken).ConfigureAwait(false);
            if (media != null)
                return media.Id;
        }

        // TODO: image size limit
        var processedFile = await DownloadImageToFile(new Uri(imageUrl), cancellationToken).ConfigureAwait(false);
        return await SaveFileToMedia(imageUrl, processedFile, cancellationToken).ConfigureAwait(false);
    }

    private async Task ScheduleUpdateIfRequired(string imageUrl, CancellationToken cancellationToken)
    {
        var grabStatus = await GrabStatusesBackend.GetByUrl(imageUrl, cancellationToken).ConfigureAwait(false);
        if (!NeedsUpdate(grabStatus))
            return;

        await Flows
            .Reset<PreviewThumbnailUpdateFlow>(
                PreviewThumbnailUpdateFlow.GetArguments(imageUrl),
                GetUpdatePeriod(grabStatus),
                "Schedule update",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProcessedFile?> DownloadImageToFile(Uri uri, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(Settings.ImageDownloadTimeout);
        return await Download(cts.Token).ConfigureAwait(false);

        async Task<ProcessedFile?> Download(CancellationToken cancellationToken1)
        {
            HttpResponseMessage response;
            try {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (OrdinalIgnoreCaseEquals(uri.DnsSafeHost, "opengraph.githubassets.com") && !Settings.GithubApiKey.IsNullOrEmpty())
                    request.Headers.Authorization = AuthenticationHeaderValue.Parse($"Bearer {Settings.GithubApiKey}");
                response = await HttpClient.SendAsync(request, cancellationToken1).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to get an image with url='{ImageUrl}'", uri);
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

    private async Task<MediaId?> SaveFileToMedia(
        string imageUrl,
        ProcessedFile? processedFile,
        CancellationToken cancellationToken)
    {
        if (processedFile is null || !MediaTypeExt.IsSupportedImage(processedFile.File.ContentType))
            return null;

        var mediaIdScope = GetMediaIdScope(imageUrl);
        var mediaLid = await processedFile.File.Process(async stream => {
            var hash = await stream.Hash().SHA256(cancellationToken).ConfigureAwait(false);
            return hash.AlphaNumeric();
        }).ConfigureAwait(false);
        var mediaId = MediaId.New(mediaIdScope, mediaLid);
        var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (media is not null) {
            await SaveGrabStatus(imageUrl, true, cancellationToken).ConfigureAwait(false);
            return media.Id;
        }

        await MediaSaver.Save(mediaId, processedFile.File, processedFile.Size, cancellationToken).ConfigureAwait(false);
        await SaveGrabStatus(imageUrl, true, cancellationToken).ConfigureAwait(false);

        return mediaId;
    }

    private static string GetMediaIdScope(string imageUrl)
        => imageUrl.Hash(Encoding.UTF8).SHA256().AlphaNumeric();

    private Task<GrabStatus> SaveGrabStatus(string imageUrl, bool success, CancellationToken cancellationToken) {
        var cmd = new GrabStatusesBackend_Change(GrabStatus.ComposeId(imageUrl), success);
        return Commander.Call(cmd, true, cancellationToken);
    }

    private bool NeedsUpdate(GrabStatus? grabStatus) => grabStatus is null
        || grabStatus.ModifiedAt + GetUpdatePeriod(grabStatus) < Clocks.SystemClock.Now;

    private TimeSpan GetUpdatePeriod(GrabStatus? grabStatus)
        => grabStatus?.IsSuccessful != false
            ? Settings.LinkPreviewUpdatePeriod
            : TimeSpan.FromHours(1);
}
