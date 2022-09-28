using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

public class ContentController : ControllerBase
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
        var byteStream = await blobStorage.OpenReadAsync(blobId, cancellationToken).ConfigureAwait(false);
        if (byteStream == null)
            return NotFound();

        var blob = (await blobStorage.GetBlobsAsync(new[] {blobId}, cancellationToken).ConfigureAwait(false)).Single();
        string contentType = MediaTypeNames.Application.Octet;
        if (blob != null && blob.IsFile && blob.Metadata.TryGetValue(Constants.Headers.ContentType, out var metadataContentType))
            contentType = metadataContentType;
        return File(byteStream, contentType);
    }
}
