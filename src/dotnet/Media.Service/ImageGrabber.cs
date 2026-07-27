using System.Net.Http.Headers;
using System.Text;
using ActualChat.Flows;
using ActualChat.Hashing;
using ActualChat.Media.Flows;
using ActualChat.Media.Module;
using ActualChat.Uploads;
using ActualLab.IO;

namespace ActualChat.Media;

public class ImageGrabber(IServiceProvider services)
{
    public const string HttpClientName = nameof(ImageGrabber);
    private MediaSettings Settings => field ??= services.GetRequiredService<MediaSettings>();
    private IMediaBackend MediaBackend => field ??= services.GetRequiredService<IMediaBackend>();
    private IGrabStatusesBackend GrabStatusesBackend => field ??= services.GetRequiredService<IGrabStatusesBackend>();
    private IMediaSaver MediaSaver { get; } = services.GetRequiredService<IMediaSaver>();
    private HttpClient HttpClient => field ??= services.HttpClientFactory().CreateClient(HttpClientName);
    private IMediaProcessor MediaProcessor { get; } = services.GetRequiredService<IMediaProcessor>();
    private IMeshLocks MeshLocks => field ??= services.MeshLocks().WithKeyPrefix(nameof(ImageGrabber));
    private ICommander Commander => field ??= services.Commander();
    private FlowHub FlowHub => field ??= services.FlowHub();
    private MomentClockSet Clocks => field ??= services.Clocks();
    private ILogger Log => field ??= services.LogFor(GetType());

    public async Task<MediaId?> GetOrGrab(string imageUrl, CancellationToken cancellationToken)
    {
        if (imageUrl.IsNullOrEmpty())
            return null;

        var existingId = await GetExisting().ConfigureAwait(false);
        if (existingId != null) {
            await FlowHub
                .TryScheduleUpdate<PreviewThumbnailUpdateFlow>(imageUrl, cancellationToken)
                .ConfigureAwait(false);
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

    // Private members

    private bool NeedsUpdate(GrabStatus? grabStatus) => grabStatus is null
        || grabStatus.ModifiedAt + GetUpdatePeriod(grabStatus) < Clocks.SystemClock.Now;

    private TimeSpan GetUpdatePeriod(GrabStatus? grabStatus)
        => grabStatus?.IsSuccessful != false
            ? Settings.LinkPreviewUpdatePeriod
            : TimeSpan.FromHours(1);

    private async Task<MediaId?> GrabUnsafe(string imageUrl, CancellationToken cancellationToken)
    {
        var grabStatus = await GrabStatusesBackend.GetByUrl(imageUrl, cancellationToken).ConfigureAwait(false);
        if (!NeedsUpdate(grabStatus)) {
            var media = await MediaBackend.GetByMediaIdScope(GetMediaIdScope(imageUrl), cancellationToken).ConfigureAwait(false);
            if (media != null)
                return media.Id;
        }

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || !EgressHttpHandler.IsHttpUri(uri))
            return null;

        var downloadedFile = await DownloadImageToFile(uri, cancellationToken).ConfigureAwait(false);
        if (downloadedFile is null)
            return null;

        var processedFile = await MediaProcessor.ProcessUpload(downloadedFile, MediaKind.LinkPreviewPicture, null, cancellationToken).ConfigureAwait(false);
        if (!MediaTypeExt.IsSupportedImage(processedFile.File.ContentType))
            return null;

        var mediaId = await SaveFileToMedia(imageUrl, processedFile, cancellationToken).ConfigureAwait(false);
        await SaveGrabStatus(imageUrl, true, cancellationToken).ConfigureAwait(false);
        return mediaId;
    }

    private async Task<UploadedFile?> DownloadImageToFile(Uri uri, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(Settings.ImageDownloadTimeout);
        return await Download(cts.Token).ConfigureAwait(false);

        async Task<UploadedFile?> Download(CancellationToken cancellationToken1)
        {
            HttpResponseMessage response;
            try {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (string.Equals(uri.DnsSafeHost, "opengraph.githubassets.com", StringComparison.OrdinalIgnoreCase) && !Settings.GithubApiKey.IsNullOrEmpty())
                    request.Headers.Authorization = AuthenticationHeaderValue.Parse($"Bearer {Settings.GithubApiKey}");
                response = await HttpClient.SendAsync(request, cancellationToken1).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to get an image with url='{ImageUrl}'", uri);
                return null;
            }

            return ConvertResponseToFile(response, cancellationToken1);
        }
    }

    private static UploadedFile? ConvertResponseToFile(HttpResponseMessage response, CancellationToken cancellationToken1)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var ext = MediaTypeExt.GetFileExtension(contentType); // TODO: convert if icon is not supported
        if (ext.IsNullOrEmpty())
            return null;

        var lastSegment = response.RequestMessage!.RequestUri!.Segments[^1].TrimSuffix(ext);
        FilePath fileName = new string(lastSegment.Where(Alphabet.AlphaNumeric.IsMatch).ToArray()) + ext;
        return new UploadedStreamFile(fileName, contentType, response.Content.Headers.ContentLength ?? 0, () => response.Content.ReadAsStreamAsync(cancellationToken1));
    }

    private async Task<MediaId> SaveFileToMedia(
        string imageUrl,
        ProcessedFile processedFile,
        CancellationToken cancellationToken)
    {
        var mediaId = await GetMediaId(imageUrl, processedFile.File, cancellationToken).ConfigureAwait(false);
        // NOTE: mediaId is constructed from imageUrl hash and from the file content hash.
        // So it should be unique for the same image content and url.
        // If there is existing media with the same id, it means that the image was already grabbed and saved.
        var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (media is not null)
            return media.Id;

        await MediaSaver.Save(mediaId, processedFile.File, processedFile.Size, MediaKind.LinkPreviewPicture, cancellationToken).ConfigureAwait(false);
        return mediaId;
    }

    private static async Task<MediaId> GetMediaId(string imageUrl, UploadedFile file, CancellationToken cancellationToken)
    {
        var mediaIdScope = GetMediaIdScope(imageUrl);
        var mediaLid = await file.Process(async stream => {
            var hash = await stream.Hash().SHA256(cancellationToken).ConfigureAwait(false);
            return hash.AlphaNumeric();
        }).ConfigureAwait(false);
        var mediaId = MediaId.New(mediaIdScope, mediaLid);
        return mediaId;
    }

    private static string GetMediaIdScope(string imageUrl)
        => imageUrl.Hash(Encoding.UTF8).SHA256().AlphaNumeric();

    private Task<GrabStatus> SaveGrabStatus(string imageUrl, bool success, CancellationToken cancellationToken) {
        var cmd = new GrabStatusesBackend_Change(GrabStatus.ComposeId(imageUrl), success);
        return Commander.Call(cmd, true, cancellationToken);
    }
}
