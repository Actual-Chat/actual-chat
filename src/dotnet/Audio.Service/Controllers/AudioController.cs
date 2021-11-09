using ActualChat.Blobs;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Audio.Controllers;

[Route("api/[controller]/[action]")]
public class AudioController : ControllerBase
{
    private readonly IBlobStorageProvider _blobs;

    public AudioController(IBlobStorageProvider blobs)
        => _blobs = blobs;

    [HttpGet("{**blobId}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "blobId" })]
    public async Task<FileStreamResult> Download(string blobId, CancellationToken cancellationToken)
    {
        var blobStorage = _blobs.GetBlobStorage(BlobScope.AudioRecord);
        var blobStream = await blobStorage.OpenReadAsync(blobId, cancellationToken);
        return File(blobStream, "audio/webm");
    }
}
