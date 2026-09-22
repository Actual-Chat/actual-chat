using System.Text;
using ActualChat.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace ActualChat.Chat;

public static class WebHookInboundEndpointExt
{
    private const string RouteTemplate = "/hooks/in/{token}";

    public static IEndpointConventionBuilder? MapWebHookInbound(this WebApplication app)
    {
        if (!app.Services.HostInfo().HasRole(HostRole.Api))
            return null;

        // Senders authenticate by the URL token alone, so there is no session and no antiforgery token
        return app.MapPost(RouteTemplate, Handle).DisableAntiforgery();
    }

    // Private methods

    private static async Task<IResult> Handle(string token, HttpContext httpContext, WebHookInbox inbox)
    {
        var request = httpContext.Request;
        var cancellationToken = httpContext.RequestAborted;
        httpContext.Response.Headers.CacheControl = "no-store";
        if (request.ContentLength is > Constants.WebHooks.InboundBodyLimit)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        // ContentLength can lie or be absent, so the cap is enforced on what's actually read:
        // one byte over the limit is enough to tell "too big" from "exactly at the limit".
        var buffer = new byte[Constants.WebHooks.InboundBodyLimit + 1];
        var byteCount = await request.Body
            .ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken)
            .ConfigureAwait(false);
        if (byteCount > Constants.WebHooks.InboundBodyLimit)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var body = Encoding.UTF8.GetString(buffer, 0, byteCount);
        InboundResult result;
        try {
            result = await inbox
                .Post(token, request.ContentType ?? "", body, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            // Nothing identifying is logged here, and nothing reaches ExceptionHandlerMiddleware,
            // which would log the request path - and the path is the plaintext token.
            httpContext.RequestServices.LogFor(typeof(WebHookInboundEndpointExt))
                .LogWarning(e, "Inbound web hook post failed");
            result = new InboundResult(503, WebHookInbox.UnavailableError);
        }

        if (result.RetryAfter is { } retryAfter)
            httpContext.Response.SetRetryAfter(retryAfter);

        return result.StatusCode switch {
            200 => Results.Json(new { ok = true, id = result.EntryLocalId }),
            404 => Results.NotFound(),
            _ => Results.Json(new { ok = false, error = result.Error }, statusCode: result.StatusCode),
        };
    }
}
