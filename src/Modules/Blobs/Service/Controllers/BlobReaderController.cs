using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Authentication;
using Stl.Fusion.Server;
using Stl.Serialization;

namespace ActualChat.Blobs.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController, JsonifyErrors]
    public class BlobReaderController : ControllerBase, IBlobReader
    {
        private readonly IBlobReader _blobReader;
        private readonly ISessionResolver _sessionResolver;

        public BlobReaderController(IBlobReader blobReader, ISessionResolver sessionResolver)
        {
            _blobReader = blobReader;
            _sessionResolver = sessionResolver;
        }

        // Commands

        [HttpGet, Publish]
        public Task<BlobInfo?> GetInfo(Session session, string blobId, CancellationToken cancellationToken = default)
        {
            session ??= _sessionResolver.Session;
            return _blobReader.GetInfo(session, blobId, cancellationToken);
        }

        [HttpGet, Publish]
        public Task<Base64Encoded> ReadTail(Session session, string blobId, long maxLength, CancellationToken cancellationToken = default)
        {
            session ??= _sessionResolver.Session;
            return _blobReader.ReadTail(session, blobId, maxLength, cancellationToken);
        }

        [HttpGet, Publish]
        public Task<Base64Encoded> Read(Session session, string blobId, long start, long maxLength, CancellationToken cancellationToken = default)
        {
            session ??= _sessionResolver.Session;
            return _blobReader.Read(session, blobId, start, maxLength, cancellationToken);
        }
    }
}
