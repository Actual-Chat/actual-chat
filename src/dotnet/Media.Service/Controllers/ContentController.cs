using System.Net.Mime;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Media.Controllers;

[ApiController, Route("api/content")]
public sealed class ContentController(IBlobStorages blobStorages) : ControllerBase
{
    private IBlobStorages BlobStorages { get; } = blobStorages;

    [HttpGet("{**blobId}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, VaryByQueryKeys = ["blobId"])]
    [EnableCors("CDN")]
    public async Task<ActionResult> Download(string blobId, CancellationToken cancellationToken)
    {
        if (blobId.IsNullOrEmpty())
            return NotFound();

        var blobStorage = BlobStorages[BlobScope.ContentRecord];
        var byteStream = await blobStorage.Read(blobId, cancellationToken).ConfigureAwait(false);
        if (byteStream == null)
            return NotFound();

        var contentType = await blobStorage.GetContentType(blobId, cancellationToken).ConfigureAwait(false);
        // stream will be disposed by the asp.net framework
        return File(byteStream, contentType ?? MediaTypeNames.Application.Octet);
    }
}
