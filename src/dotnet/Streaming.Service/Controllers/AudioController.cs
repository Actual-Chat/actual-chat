using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Streaming.Controllers;

[ApiController, Route("api/audio")]
public sealed class AudioController(IBlobStorages blobStorages) : ControllerBase
{
    private IBlobStorages BlobStorages { get; } = blobStorages;

    [HttpGet("download/{**blobId}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client, VaryByQueryKeys = [ "blobId" ])]
    public async Task<ActionResult > Download(string blobId, CancellationToken cancellationToken)
    {
        var blobStorage = BlobStorages[BlobScope.AudioRecord];
        var byteStream = await blobStorage.Read(blobId, cancellationToken).ConfigureAwait(false);
        if (byteStream == null)
            return NotFound();

        // stream will be disposed by the asp.net framework
        return File(byteStream, "audio/webm");
    }
}
