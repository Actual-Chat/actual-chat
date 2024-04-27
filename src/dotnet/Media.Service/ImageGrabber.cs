using System.Text;
using ActualChat.Hashing;
using ActualChat.Media.Module;
using ActualChat.Uploads;
using ActualLab.IO;

namespace ActualChat.Media;

public class ImageGrabber(
    MediaSettings settings,
    IHttpClientFactory httpClientFactory,
    IMediaBackend mediaBackend,
    IContentSaver contentSaver,
    ICommander commander,
    IEnumerable<IUploadProcessor> uploadProcessors)
{
    private HttpClient HttpClient { get; } = httpClientFactory.CreateClient(Crawler.HttpClientName);
    private IReadOnlyList<IUploadProcessor> UploadProcessors => uploadProcessors.ToList();

    public async Task<MediaId> GrabImage(string imageUri, CancellationToken cancellationToken)
    {
        if (imageUri.IsNullOrEmpty())
            return MediaId.None;

        // TODO: image size limit
        var processedFile = await DownloadImageToFile(imageUri, cancellationToken).ConfigureAwait(false);
        return await SaveFileToMedia(imageUri, processedFile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaId> GrabImage(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var file = await SaveImageToFile(response, cancellationToken).ConfigureAwait(false);
        return await SaveFileToMedia(response.RequestMessage!.RequestUri!.AbsoluteUri, file, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MediaId> SaveFileToMedia(
        string imageUri,
        ProcessedFile? processedFile,
        CancellationToken cancellationToken)
    {
        if (processedFile is null || !MediaTypeExt.IsSupportedImage(processedFile.File.ContentType))
            return MediaId.None;

        var mediaIdScope = imageUri.Hash(Encoding.UTF8).SHA256().AlphaNumeric();
        var mediaLid = await processedFile.File.Process(async stream => {
            var hash = await stream.Hash().SHA256(cancellationToken).ConfigureAwait(false);
            return hash.AlphaNumeric();
        }).ConfigureAwait(false);
        var mediaId = new MediaId(mediaIdScope, mediaLid);
        var media = await mediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (media is not null)
            return media.Id;

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
        await contentSaver.Save(content, cancellationToken).ConfigureAwait(false);

        var changeCommand = new MediaBackend_Change(
            mediaId,
            new Change<Media> {
                Create = media,
            });
        await commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        return mediaId;
    }

    private async Task<ProcessedFile?> DownloadImageToFile(string imageUrl, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(settings.ImageDownloadTimeout);
        return await Download(cts.Token).ConfigureAwait(false);

        async Task<ProcessedFile?> Download(CancellationToken cancellationToken1)
        {
            var response = await HttpClient.GetAsync(imageUrl, cancellationToken1).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

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
}
