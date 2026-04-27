using ActualLab.Fusion.Server;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

// Backwards-compat shim for old MAUI clients that still call the REST endpoint.
// New clients should use INativeAuth via RPC. Once telemetry shows no traffic, delete this.

/// <summary>
/// Legacy HTTP entry points for native (iOS/Android) sign-in. Delegates to
/// <see cref="INativeAuth"/> via <c>Commander</c>; kept only so previously
/// installed MAUI app versions (with a baked-in RestEase <c>INativeAuthClient</c>)
/// continue to work after the RPC migration.
/// </summary>
[Obsolete("2026.04: Old MAUI clients only. Remove once no installed app version targets this route.")]
[ApiController, Route("api/native-auth"), JsonifyErrors]
public sealed class LegacyNativeAuthController(IServiceProvider services) : ControllerBase
{
    private ICommander Commander { get; } = services.Commander();

    [HttpGet("sign-in-apple")]
    public Task SignInApple(
        string userId,
        string code,
        string? email,
        string? name,
        bool mustExist = false,
        CancellationToken cancellationToken = default)
    {
        var session = HttpContext.GetSessionFromHeader();
        var command = new NativeAuth_SignInApple(session, userId, code, email, name, mustExist);
        return Commander.Call(command, true, cancellationToken);
    }

    [HttpGet("sign-in-google")]
    public Task SignInGoogle(string code, bool mustExist = false, CancellationToken cancellationToken = default)
    {
        var session = HttpContext.GetSessionFromHeader();
        var command = new NativeAuth_SignInGoogle(session, code, mustExist);
        return Commander.Call(command, true, cancellationToken);
    }
}
