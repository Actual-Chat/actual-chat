using System.Net.Mime;
using ActualChat.AspNetCore;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Media.Controllers;

[ApiController, Route("api/content")]
public sealed class ContentController(IBlobStorages blobStorages, IMediaBackend mediaBackend) : ControllerBase
{
    private static readonly IReadOnlySet<string> InlineContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "image/avif",
            "image/bmp",
            "image/gif",
            "image/heic",
            "image/heif",
            "image/jpeg",
            "image/png",
            "image/tiff",
            "image/vnd.microsoft.icon",
            "image/webp",
            "audio/3gpp",
            "audio/aac",
            "audio/aiff",
            "audio/amr",
            "audio/flac",
            "audio/midi",
            "audio/mp4",
            "audio/mpeg",
            "audio/ogg",
            "audio/opus",
            "audio/wav",
            "audio/webm",
            "video/3gpp",
            "video/avi",
            "video/mp4",
            "video/mpeg",
            "video/ogg",
            "video/quicktime",
            "video/webm",
            "video/x-flv",
            "video/x-matroska",
        };

    [HttpGet("{**blobId}")]
    // Will NOT be cached in memory by response cache middleware
    [CacheControlImmutable(Duration = 2592000 /*30 days*/)]
    [EnableCors("CDN")]
    public async Task<ActionResult> Download(
        string blobId,
        [FromQuery] string? download,
        CancellationToken cancellationToken)
    {
        // Presence means "download". It's bound as a string rather than a bool so that a stray
        // value can't trip [ApiController]'s model validation into a 400 on a public CDN URL.
        var isDownload = download is not null;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (blobId.IsNullOrEmpty())
            return NotFound();
        if (string.Equals(
                BlobPath.GetScope(blobId),
                BlobScope.AudioRecord.Value,
                StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var media = await mediaBackend.GetByBlobId(blobId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return NotFound();

        var blobStorage = blobStorages[BlobScope.ContentRecord];
        var byteStream = await blobStorage.Read(blobId, cancellationToken).ConfigureAwait(false);
        if (byteStream == null)
            return NotFound();

        var contentType = await BlobContentTypeDetector.Detect(byteStream, cancellationToken).ConfigureAwait(false)
            ?? MediaTypeNames.Application.Octet;
        return InlineContentTypes.Contains(contentType) && !isDownload
            ? File(byteStream, contentType, enableRangeProcessing: true)
            : File(byteStream, contentType, media.FileName.NullIfEmpty() ?? "download", enableRangeProcessing: true);
    }
}
