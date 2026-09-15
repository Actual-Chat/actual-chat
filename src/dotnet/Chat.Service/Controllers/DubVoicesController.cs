using ActualChat.Security;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

[ApiController, Route("api/dub-voices")]
public sealed class DubVoicesController(IServiceProvider services) : ControllerBase
{
    private const string ContentType = "audio/mpeg";
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(1);

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private ITranslationsBackend TranslationsBackend { get; } = services.GetRequiredService<ITranslationsBackend>();

    [HttpGet("{voiceId}/preview")]
    public async Task<IActionResult> Preview(
        string voiceId,
        [FromQuery] string? language,
        CancellationToken cancellationToken)
    {
        try {
            // Header: the app; query: the token a preview URL carries in a MAUI WebView; cookie: SSB
            var session = HttpContext.TryGetSessionFromHeader(SessionFormat.Token)
                ?? HttpContext.TryGetSessionFromQuery()
                ?? HttpContext.GetSessionFromCookie();
            await Accounts.GetOwn(session, cancellationToken).Require(AccountFull.MustBeActive).ConfigureAwait(false);
        }
        catch (Exception e) {
            return BadRequest(e.Message);
        }
        if (Language.TryParse(language) is not { } previewLanguage)
            return BadRequest($"Invalid language: '{language}'");

        var mp3 = await TranslationsBackend.GetDubVoicePreview(voiceId, previewLanguage, cancellationToken)
            .ConfigureAwait(false);
        if (mp3 == null)
            return NotFound($"Unknown voice: '{voiceId}'");

        Response.Headers.CacheControl = $"private, max-age={(int)CacheMaxAge.TotalSeconds}";
        return File(mp3, ContentType);
    }
}
