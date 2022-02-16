using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Content.Controllers;

[Route("api/[controller]/[action]")]
public class ContentController : ControllerBase
{
    private readonly IBlobStorageProvider _blobs;

    public ContentController(IBlobStorageProvider blobs)
        => _blobs = blobs;

    [HttpGet("{**blobId}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "blobId" })]
    public async Task<ActionResult> Download(string blobId, CancellationToken cancellationToken)
    {
        var blobStorage = _blobs.GetBlobStorage(BlobScope.ContentRecord);
        var byteStream = await blobStorage.OpenReadAsync(blobId, cancellationToken);
        if (byteStream == null)
            return NotFound();
        var blob = (await blobStorage.GetBlobsAsync(new[] {blobId}, cancellationToken).ConfigureAwait(false)).Single();
        string contentType = "application";
        if (blob != null && blob.IsFile && blob.Metadata.TryGetValue(Constants.Metadata.ContentType, out var metadataContentType))
            contentType = metadataContentType;
        return File(byteStream, contentType);
    }
}
