using System.Text;
using ActualChat.Hashing;
using ActualChat.Security;
using ActualChat.Users;
using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.MLSearch.Bot.Tools;

public static class AuthSchemes {
    public const string BotAuthenticationScheme = "Bot";
}



[ApiController, Route("api/bot/sample-tool")]
//[Authorize(AuthenticationSchemes = AuthSchemes.BotAuthenticationScheme)]
public sealed class SampleToolController : ControllerBase
{

    public SampleToolController(IAccounts accounts, ICommander commander): base()
    {

    }

    /// <summary>
    /// Function retuns todays date in a format specified.
    /// </summary>
    /// <param name="format">Format for the day</param>
    /// <returns>Date in a format specified.</returns>
    [HttpPost("today")]
    public async Task<ActionResult<string>> Today([FromBody]string? format, CancellationToken cancellationToken)
    {
        var today = DateTime.Now;
        try {
            var result = today.ToString(format);
            return Ok(result);
        }
        catch (System.FormatException exception) {
            return BadRequest(exception.Message);
        }
    }
}