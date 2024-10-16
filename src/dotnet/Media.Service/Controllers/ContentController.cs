using System.Net.Mime;
using ActualChat.AspNetCore;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Media.Controllers;

[ApiController, Route("api/content")]
public sealed class ContentController(IBlobStorages blobStorages, IMediaBackend mediaBackend) : ControllerBase
{
    [HttpGet("{**blobId}")]
    // Will NOT be cached in memory by response cache middleware
    [CacheControlImmutable(Duration = 2592000 /*30 days*/)]
    [EnableCors("CDN")]
    public async Task<ActionResult> Download(string blobId, CancellationToken cancellationToken)
    {
        if (blobId.IsNullOrEmpty())
            return NotFound();

        var blobStorage = blobStorages[BlobScope.ContentRecord];
        var byteStream = await blobStorage.Read(blobId, cancellationToken).ConfigureAwait(false);
        if (byteStream == null)
            return NotFound();

        var media = await mediaBackend.GetByContentId(blobId, cancellationToken).ConfigureAwait(false);
        if (media != null) {
            var mediaContentType = media.ContentType.IsNullOrEmpty()
                ? MediaTypeNames.Application.Octet
                : media.ContentType;
            return File(byteStream, mediaContentType, media.FileName);
        }

        var contentType = await blobStorage.GetContentType(blobId, cancellationToken).ConfigureAwait(false);
        // stream will be disposed by the asp.net framework
        return File(byteStream, contentType ?? MediaTypeNames.Application.Octet);
    }
}
