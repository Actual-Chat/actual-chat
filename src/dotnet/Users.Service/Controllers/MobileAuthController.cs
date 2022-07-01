using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Stl.CommandR.Internal;
using Stl.Fusion.Authentication;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.Server.Authentication;
using Stl.Fusion.Server.Internal;

namespace ActualChat.Chat.Controllers;

[Route("mobileauth")]
[ApiController]
public class MobileAuthController : Controller
{
    const string callbackScheme = "xamarinessentials";

    private readonly IServiceProvider _services;

    public MobileAuthController(IServiceProvider services)
    {
        _services = services;
    }

    // Example is taken from https://github.com/dotnet/maui/blob/main/src/Essentials/samples/Sample.Server.WebAuthenticator/Controllers/MobileAuthController.cs
    [HttpGet("{scheme}")]
    public async Task Get([FromRoute] string scheme)
    {
        var auth = await Request.HttpContext.AuthenticateAsync(scheme);

        if (!auth.Succeeded
            || auth?.Principal == null
            || !auth.Principal.Identities.Any(id => id.IsAuthenticated)
            || string.IsNullOrEmpty(auth.Properties.GetTokenValue("access_token"))) {
            // Not authenticated, challenge
            await Request.HttpContext.ChallengeAsync(scheme);
        }
        else {
            var claims = auth.Principal.Identities.FirstOrDefault()?.Claims;
            var email = string.Empty;
            email = claims?.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;

            // Get parameters to send back to the callback
            var qs = new Dictionary<string, string>
            {
                { "access_token", auth.Properties.GetTokenValue("access_token") },
                { "refresh_token", auth.Properties.GetTokenValue("refresh_token") ?? string.Empty },
                { "expires_in", (auth.Properties.ExpiresUtc?.ToUnixTimeSeconds() ?? -1).ToString() },
                { "email", email }
            };

            // Build the result url
            var url = callbackScheme + "://#" + string.Join(
                "&",
                qs.Where(kvp => !string.IsNullOrEmpty(kvp.Value) && kvp.Value != "-1")
                .Select(kvp => $"{WebUtility.UrlEncode(kvp.Key)}={WebUtility.UrlEncode(kvp.Value)}"));

            // Redirect to final url
            Request.HttpContext.Response.Redirect(url);
        }
    }

    [HttpGet("signIn/{sessionId}/{scheme}")]
    public async Task SignIn(string sessionId, string scheme)
    {
        if (HttpContext.User == null
            || !HttpContext.User.Identities.Any(id => id.IsAuthenticated)) {
            // Not authenticated, challenge
            await Request.HttpContext.ChallengeAsync(scheme).ConfigureAwait(false);
        }
        else {
            var sessionResolver = new SimpleSessionResolver(new Session(sessionId));
            var helper = new ServerAuthHelper(
               _services.GetService<ServerAuthHelper.Options>(),
               _services.GetRequiredService<IAuth>(),
               _services.GetRequiredService<IAuthBackend>(),
               sessionResolver,
               _services.GetRequiredService<AuthSchemasCache>(),
               _services.GetRequiredService<ICommander>(),
               _services.GetRequiredService<MomentClockSet>()
            );
            await helper.UpdateAuthState(HttpContext).ConfigureAwait(false);

            string responseString = "<html><head></head><body>We are done, please, return to the app.<script>setTimeout(function() { window.close(); }, 1000)</script></body></html>";
            HttpContext.Response.ContentType = "text/html; charset=utf-8";
            await HttpContext.Response.WriteAsync(responseString).ConfigureAwait(false);
        }
    }

    [HttpGet("signOut/{sessionId}")]
    public async Task SignOut(string sessionId)
    {
        // TODO: test
        var auth = _services.GetRequiredService<IAuth>();
        var commander = _services.GetRequiredService<ICommander>();
        var session = new Session(sessionId);
        var command = new SignOutCommand(session);
        try {
            var signOutCommand = new SignOutCommand(session);
            await commander.Call(signOutCommand).ConfigureAwait(false);
        }
        finally {
            // Ideally this should be done once important things are completed
            _ = Task.Run(() => auth.UpdatePresence(session, default), default);
        }
    }

    private class SimpleSessionResolver : ISessionResolver
    {
        public SimpleSessionResolver(Session session)
            => SessionTask = Task.FromResult(session);

        public Session Session {
            get => SessionTask.Result;
            set => throw new NotSupportedException();
        }

        public Task<Session> SessionTask { get; }

        public bool HasSession => true;

        public Task<Session> GetSession(CancellationToken cancellationToken = default) => SessionTask;
    }
}
