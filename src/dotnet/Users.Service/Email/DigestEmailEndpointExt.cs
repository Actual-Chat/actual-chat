using System.Net;
using ActualChat.Flows;
using ActualChat.Users.Flows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace ActualChat.Users.Email;

/// <summary>
/// One-click digest unsubscribe reached from the email footer and from RFC 8058
/// List-Unsubscribe headers, so it is anonymous and needs no session.
/// </summary>
public static class DigestEmailEndpointExt
{
    private const string UnsubscribeRoute = "/emails/digest/{token}/unsubscribe";
    private const string ResubscribeRoute = "/emails/digest/{token}/resubscribe";

    public static string GetUnsubscribePath(string token)
        => UnsubscribeRoute.Replace("{token}", token);

    public static void MapDigestEmail(this WebApplication app)
    {
        if (!app.Services.HostInfo().HasRole(HostRole.Api))
            return;

        // Mail clients and link scanners open the footer link with GET; the RFC 8058 header uses POST
        app.MapGet(UnsubscribeRoute, Unsubscribe).DisableAntiforgery();
        app.MapPost(UnsubscribeRoute, Unsubscribe).DisableAntiforgery();
        app.MapPost(ResubscribeRoute, Resubscribe).DisableAntiforgery();
    }

    // Private methods

    private static Task<IResult> Unsubscribe(string token, HttpContext httpContext)
        => SetDigestEnabled(token, false, httpContext);

    private static Task<IResult> Resubscribe(string token, HttpContext httpContext)
        => SetDigestEnabled(token, true, httpContext);

    private static async Task<IResult> SetDigestEnabled(string token, bool isEnabled, HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        var services = httpContext.RequestServices;
        var cancellationToken = httpContext.RequestAborted;
        var userId = services.GetRequiredService<DigestUnsubscribeTokens>().TryParse(token);
        if (userId is null)
            return Results.NotFound();

        var account = await services.GetRequiredService<IAccountsBackend>()
            .Get(userId, cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
            return Results.NotFound();

        var settings = services.GetRequiredService<IServerKvasBackend>().ForUser(userId).UserEmailsSettings();
        var emailsSettings = await settings.Get(cancellationToken).ConfigureAwait(false);
        if (emailsSettings.IsDigestEnabled != isEnabled) {
            await settings.Set(emailsSettings with { IsDigestEnabled = isEnabled }, cancellationToken)
                .ConfigureAwait(false);
            var source = HttpMethods.IsPost(httpContext.Request.Method) ? "link_post" : "link_get";
            EmailMeters.RecordSubscription(isEnabled, source);
        }
        if (isEnabled)
            await services.FlowHub()
                .NewResumeEvent<DigestFlow>(userId.Value)
                .Schedule(cancellationToken)
                .ConfigureAwait(false);

        var signedInAs = await GetSignedInAs(userId, httpContext).ConfigureAwait(false);
        var baseUrl = services.UrlMapper().BaseUrl;
        var recipient = account.Email.NullIfEmpty() ?? account.Name;
        var html = RenderPage(token, isEnabled, recipient, signedInAs, baseUrl);
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<string?> GetSignedInAs(UserId userId, HttpContext httpContext)
    {
        // The link is anonymous: it acts on the account the email went to, while the browser may
        // be signed in as someone else, who would then look for the change in the wrong Settings
        var session = httpContext.TryGetSessionFromCookie();
        if (session is null)
            return null;

        var ownAccount = await httpContext.RequestServices.GetRequiredService<IAccounts>()
            .GetOwn(session, httpContext.RequestAborted)
            .ConfigureAwait(false);
        if (ownAccount.IsGuest || ownAccount.Id == userId)
            return null;

        return ownAccount.Email.NullIfEmpty() ?? ownAccount.Name;
    }

    private static string RenderPage(
        string token, bool isEnabled, string recipient, string? signedInAs, string baseUrl)
    {
        var appName = WebUtility.HtmlEncode(CoreConstants.AppName);
        var encodedToken = WebUtility.HtmlEncode(token);
        var encodedRecipient = WebUtility.HtmlEncode(recipient);
        var (title, text, action, actionText) = isEnabled
            ? ("Digest emails are on again",
                $"<b>{encodedRecipient}</b> will keep receiving the daily digest of unread chats in {appName}.",
                GetUnsubscribePath(encodedToken),
                "Turn off")
            : ("Digest emails are off",
                $"<b>{encodedRecipient}</b> will no longer receive the daily digest of unread chats from {appName}.",
                ResubscribeRoute.Replace("{token}", encodedToken),
                "Undo");
        var note = signedInAs is null
            ? ""
            : $"<p class=\"note\">This browser is signed in as <b>{WebUtility.HtmlEncode(signedInAs)}</b>, "
                + "a different account, so its Settings are unchanged.</p>";
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>{{appName}}: {{title}}</title>
            <style>
            body { margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
                   font-family: Arial, sans-serif; color: #1C1C1C; background: #F7F7FC; }
            main { max-width: 420px; padding: 32px 24px; text-align: center; }
            h1 { font-size: 22px; font-weight: 600; margin: 0 0 12px; }
            p { font-size: 15px; line-height: 22px; margin: 0 0 24px; color: #4A4A4A; }
            p.note { font-size: 13px; line-height: 18px; color: #B54708; }
            button, a.button { display: inline-block; min-width: 120px; margin: 0 6px 12px; padding: 10px 20px;
                               font-size: 15px; border-radius: 24px; border: 1px solid #E8E8E8; cursor: pointer;
                               text-decoration: none; }
            button { background: #FFF; color: #1C1C1C; }
            a.button { background: #0036A3; color: #FFF; border-color: #0036A3; }
            </style>
            </head>
            <body>
            <main>
            <h1>{{title}}</h1>
            <p>{{text}}</p>
            {{note}}
            <form method="post" action="{{action}}" style="display:inline">
            <button type="submit">{{actionText}}</button>
            </form>
            <a class="button" href="{{WebUtility.HtmlEncode(baseUrl)}}">Open {{appName}}</a>
            </main>
            </body>
            </html>
            """;
    }
}
