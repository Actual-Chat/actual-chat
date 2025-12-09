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
    private ILogger Log { get; } = services.LogFor<UploadsController>();

    private static class Headers
    {
        public const string UploadOffset = "Upload-Offset";
    }

    [HttpHead("{uploadSid}")]
    public async Task<IActionResult> Status(string uploadSid, CancellationToken cancellationToken)
    {
        if (!UploadId.TryParse(uploadSid, out var uploadId))
            return BadRequest("Invalid upload id.");
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

        var result = await Uploads.GetOffset(session, uploadId, cancellationToken).ConfigureAwait(false);
        if (result.Error is NotFoundException<Upload>)
            return NotFound();

        Response.Headers[Headers.UploadOffset] = result.Value.ToInvariantString();
        return Ok();
    }

    [HttpPatch("{uploadSid}")]
    [RequestSizeLimit(Constants.Uploads.ChuckSizeLimit + 10000 /* extra size for headers and etc. */)]
    [Consumes("application/offset+octet-stream")]
    public async Task<ActionResult> Upload(
        string uploadSid,
        [FromHeader(Name = Headers.UploadOffset)] long uploadOffset,
        CancellationToken cancellationToken)
    {
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

        if (!UploadId.TryParse(uploadSid, out var uploadId))
            return BadRequest("Invalid upload id.");

        var ms = new MemoryStream();
        await using (ms.ConfigureAwait(false)) {
            await Request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            byte[] chunk = ms.ToArray();
            var command = new Uploads_Append(session, uploadId, uploadOffset, chunk);
            var result = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            if (result.Error is NotFoundException<Upload>)
                return NotFound();

            if (result.Error is OffsetConflictException)
                return Conflict();

            Response.Headers[Headers.UploadOffset] = result.Value.ToInvariantString();
            return NoContent();
        }
    }
}
