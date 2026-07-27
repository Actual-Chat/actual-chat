using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Net.Http.Headers;

namespace ActualChat.Resilience.Internal;

/// <summary>
/// Charges controller requests against the <see cref="RateLimitPolicy"/>. Must be placed after UseRouting:
/// requests that don't map to a controller action (static files, RPC endpoints, Blazor) are skipped.
/// </summary>
public sealed class HttpRateLimitMiddleware(RequestDelegate next, IServiceProvider services)
{
    private RequestDelegate Next { get; } = next;
    private RateLimitPolicy Policy { get; } = services.GetService<RateLimitPolicy>() ?? RateLimitPolicy.Unlimited;
    private RateLimitIdentityResolver IdentityResolver { get; }
        = services.GetService<RateLimitIdentityResolver>() ?? new RateLimitIdentityResolver(services);

    public async Task Invoke(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (action is null) {
            await Next.Invoke(context).ConfigureAwait(false);
            return;
        }

        var rateLimitClass = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
            ? RateLimitClass.HttpRead
            : RateLimitClass.Command;
        var cancellationToken = context.RequestAborted;
        var identities = new RateLimitIdentity[RateLimitIdentityResolver.MaxIdentityCount];
        var identityCount = await IdentityResolver
            .Resolve(Policy, rateLimitClass, RateLimitSource.ForHttp(context), identities, cancellationToken)
            .ConfigureAwait(false);
        if (identityCount != 0) {
            var method = $"{action.ControllerName}.{action.ActionName}";
            try {
                await Policy
                    .Check(method, rateLimitClass, identities.AsSpan(0, identityCount), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RateLimitExceededException e) {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers[HeaderNames.RetryAfter] =
                    ((int)Math.Ceiling(e.RetryDelay.TotalSeconds)).Format();
                return;
            }
        }

        await Next.Invoke(context).ConfigureAwait(false);
    }
}
