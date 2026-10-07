using ActualChat.Users.Module;
using ActualChat.Users.Phone;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Twilio.Security;

namespace ActualChat.Users.Controllers;

[ApiController, Route(TwilioMessageStatuses.Route)]
[AllowAnonymous, IgnoreAntiforgeryToken]
public sealed class TwilioMessageStatusController(IServiceProvider services) : ControllerBase
{
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private TwilioMessageStatuses Statuses { get; } = services.GetRequiredService<TwilioMessageStatuses>();

    [HttpPost, Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(16_384)]
    [RequestFormLimits(ValueCountLimit = 64, ValueLengthLimit = 4096)]
    public async Task<IActionResult> Update(CancellationToken cancellationToken)
    {
        if (Settings.TwilioAuthToken.IsNullOrEmpty() || Settings.TwilioStatusCallbackUrl.IsNullOrEmpty())
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (Request.Query.Count != 1 || Request.Query["attemptId"].Count != 1)
            return BadRequest();

        var attemptId = Request.Query["attemptId"].ToString();
        if (attemptId.Length != 32 || attemptId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            return BadRequest();
        if (!Request.HasFormContentType || Request.Headers["X-Twilio-Signature"].Count != 1)
            return StatusCode(StatusCodes.Status403Forbidden);

        var form = await Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        if (form.Files.Count != 0 || form.Any(x => x.Value.Count != 1))
            return BadRequest();

        var parameters = form.ToDictionary(x => x.Key, x => x.Value.ToString());
        // Use the configured public URL, never proxy-derived host/scheme values, for signature validation.
        var publicUrl = Statuses.GetCallbackUri(attemptId).GetLeftPart(UriPartial.Path) + Request.QueryString.Value;
        var isValid = new RequestValidator(Settings.TwilioAuthToken)
            .Validate(publicUrl, parameters, Request.Headers["X-Twilio-Signature"].ToString());
        if (!isValid)
            return StatusCode(StatusCodes.Status403Forbidden);
        if (form["AccountSid"].ToString() != Settings.TwilioAccountSid)
            return StatusCode(StatusCodes.Status403Forbidden);

        var messageSid = form["MessageSid"].ToString();
        var status = form["MessageStatus"].ToString();
        if (messageSid.IsNullOrEmpty() || TwilioMessageStatuses.GetStatusRank(status) == 0)
            return BadRequest();

        await Statuses.Update(attemptId, Settings.TwilioAccountSid, messageSid, status,
            form["ErrorCode"].ToString(), cancellationToken).ConfigureAwait(false);

        return NoContent();
    }
}
