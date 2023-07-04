using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

public sealed class ContentController : ControllerBase
{
    private readonly IBlobStorageProvider _blobs;

    public ContentController(IBlobStorageProvider blobs)
        => _blobs = blobs;

    [HttpGet("api/content/{**blobId}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "blobId" })]
    public async Task<ActionResult> Download(string blobId, CancellationToken cancellationToken)
    {
        if (blobId.IsNullOrEmpty())
            return NotFound();

        var blobStorage = _blobs.GetBlobStorage(BlobScope.ContentRecord);
        var byteStream = await blobStorage.Read(blobId, cancellationToken).ConfigureAwait(false);
        if (byteStream == null)
            return NotFound();

        var contentType = await blobStorage.GetContentType(blobId, cancellationToken).ConfigureAwait(false);
        // stream will be disposed by the asp.net framework
        return File(byteStream, contentType ?? MediaTypeNames.Application.Octet);
    }
}
