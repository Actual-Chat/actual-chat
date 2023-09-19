using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Audio.Controllers;

[ApiController, Route("api/audio")]
public sealed class AudioController(IBlobStorageProvider blobs) : ControllerBase
{
    [HttpGet("download/{**blobId}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "blobId" })]
    public async Task<ActionResult > Download(string blobId, CancellationToken cancellationToken)
    {
        var blobStorage = blobs.GetBlobStorage(BlobScope.AudioRecord);
        var byteStream = await blobStorage.Read(blobId, cancellationToken).ConfigureAwait(false);
        if (byteStream == null)
            return NotFound();

        // stream will be disposed by the asp.net framework
        return File(byteStream, "audio/webm");
    }
}
