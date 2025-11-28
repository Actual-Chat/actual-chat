using ActualChat.Controllers;
using ActualChat.Media;
using ActualChat.Security;
using ActualChat.Users;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

[ApiController, Route("api/uploads")]
public sealed class UploadsController(IServiceProvider services) : ControllerBase
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IUploads Uploads { get; } = services.GetRequiredService<IUploads>();
    private ICommander Commander { get; } = services.Commander();

    private static class Headers
    {
        public const string UploadOffset = "Upload-Offset";
    }

    [HttpHead("{uploadSid}")]
    public async Task<IActionResult> Status(string uploadSid, CancellationToken cancellationToken)
    {
        var uploadId = UploadId.Parse(uploadSid);
        Session session;
        try {
            // NOTE(AY): Header is used by clients, cookie is used by SSB
            session = HttpContext.TryGetSessionFromHeader(SessionFormat.Token)
                ?? HttpContext.GetSessionFromCookie();
            await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            return BadRequest(e.Message);
        }
        var offset = await Uploads.GetOffset(session, uploadId, cancellationToken).ConfigureAwait(false);
        Response.Headers[Headers.UploadOffset] = offset.ToInvariantString();
        return Ok();
    }

    [HttpPatch("{uploadSid}")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(Constants.Uploads.ChuckSizeLimit + 200 /* extra size for headers and etc. */)]
    [Consumes("application/offset+octet-stream")]
    public async Task<ActionResult<MediaContent>> Upload(string uploadSid, [FromBody] byte[] data, CancellationToken cancellationToken)
    {
        var uploadId = UploadId.Parse(uploadSid);
        Session session;
        try {
            // NOTE(AY): Header is used by clients, cookie is used by SSB
            session = HttpContext.TryGetSessionFromHeader(SessionFormat.Token)
                ?? HttpContext.GetSessionFromCookie();
            await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            return BadRequest(e.Message);
        }

        var httpRequest = HttpContext.Request;
        long.TryParse(httpRequest.Headers[Headers.UploadOffset], CultureInfo.InvariantCulture, out var uploadOffset);
        if (uploadOffset < 0)
            return BadRequest("Invalid or missing upload offset");

        // TODO(DF): optimize memory usage
        var ms = new MemoryStream();
        await using (ms.ConfigureAwait(false)) {
            await Request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            byte[] chunk = ms.ToArray();
            var command = new Uploads_Append(session, uploadId, chunk, uploadOffset);
            var newOffset = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            Response.Headers[Headers.UploadOffset] = newOffset.ToInvariantString();
            return NoContent();
        }
    }
}
