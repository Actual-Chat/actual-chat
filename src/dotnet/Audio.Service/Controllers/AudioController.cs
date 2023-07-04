using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Audio.Controllers;

[Route("api/[controller]/[action]")]
public sealed class AudioController : ControllerBase
{
    private readonly IBlobStorageProvider _blobs;

    public AudioController(IBlobStorageProvider blobs)
        => _blobs = blobs;

    [HttpGet("{**blobId}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "blobId" })]
    public async Task<ActionResult > Download(string blobId, CancellationToken cancellationToken)
    {
        var blobStorage = _blobs.GetBlobStorage(BlobScope.AudioRecord);
        var byteStream = await blobStorage.Read(blobId, cancellationToken).ConfigureAwait(false);
        if (byteStream == null)
            return NotFound();

        // stream will be disposed by the asp.net framework
        return File(byteStream, "audio/webm");
    }
}
