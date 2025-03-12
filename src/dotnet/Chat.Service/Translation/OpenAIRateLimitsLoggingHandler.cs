using System.Net;
using System.Text;

namespace ActualChat.Chat;

public class OpenAIRateLimitsLoggingHandler(IServiceProvider? services = null) : DelegatingHandler
{
    private const string Prefix = "x-ratelimit-";

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services?.LogFor(GetType()) ?? StaticLog.For(GetType());

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return response;

        var sb = new StringBuilder();
        foreach (var header in response.Headers.Where(x => x.Key.OrdinalIgnoreCaseStartsWith(Prefix)))
            sb.AppendLine().Append(header.Key[Prefix.Length..]).Append('=').AppendJoin(',', header.Value);
        Log.LogWarning("OpenAI rate limits: {Headers}", sb);
        return response;
    }
}
