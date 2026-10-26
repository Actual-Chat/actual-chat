using System.Net;
using System.Net.Http.Headers;
using ActualChat.Maui;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using AdServices;

namespace ActualChat.App.Maui;

// Apple Ads attribution: Apple's whole answer, which the server parses. null is a final "not attributed";
// a thrown error is transient and makes AccountUI read again at the next start, which the first-try
// deadline below bounds to the 24 hours Apple keeps the token and the record for.
public sealed class IosInstallReferrer(UIHub hub) : UIServiceBase<UIHub>(hub), IInstallReferrer
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan Deadline = TimeSpan.FromHours(24);
    private static readonly TimeSpan NotFoundRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly Uri AttributionApiUri = new("https://api-adservices.apple.com/api/v1/");

    private HttpClient HttpClient
        => field ??= Hub.HttpClientFactory.CreateClient(nameof(IosInstallReferrer));

    public AttributionOrigin Origin => AttributionOrigin.AppleAdServices;

    public async Task<string?> Get(CancellationToken cancellationToken)
    {
        var now = Clocks.SystemClock.Now;
        if (MauiPreferences.AppleAttributionFirstTryAt is not { } firstTryAt) {
            firstTryAt = now;
            MauiPreferences.AppleAttributionFirstTryAt = now;
        }
        if (now - firstTryAt > Deadline) {
            Log.LogInformation("Apple Ads attribution: gave up, the first try was at {FirstTryAt}", firstTryAt);
            return null;
        }

        var token = await Task.Run(GetToken, cancellationToken).ConfigureAwait(false);
        if (token is null)
            return null;

        return await ResolveAttribution(token, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private string? GetToken()
    {
        var token = AAAttribution.GetAttributionToken(out var error);
        if (error is null)
            return token;

        var code = (AAAttributionErrorCode)(long)error.Code;
        if (code == AAAttributionErrorCode.NetworkError)
            throw StandardError.External("AdServices couldn't issue the attribution token: no network.");

        Log.LogInformation("Apple Ads attribution: no token, {Code}", code);
        return null;
    }

    private async Task<string?> ResolveAttribution(string token, CancellationToken cancellationToken)
    {
        for (var attempt = 1;; attempt++) {
            using var content = new StringContent(token, new MediaTypeHeaderValue("text/plain"));
            using var response = await HttpClient
                .PostAsync(AttributionApiUri, content, cancellationToken)
                .ConfigureAwait(false);
            switch (response.StatusCode) {
            case HttpStatusCode.OK:
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Log.LogInformation("Apple Ads attribution: {Payload}", json);
                return json;
            case HttpStatusCode.BadRequest:
                Log.LogWarning("Apple Ads attribution: Apple rejected the token");
                return null;
            case HttpStatusCode.NotFound when attempt < MaxAttempts:
                await Clocks.CpuClock.Delay(NotFoundRetryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            case HttpStatusCode.NotFound:
                Log.LogInformation("Apple Ads attribution: no record after {Attempts} attempts", attempt);
                return null;
            default:
                response.EnsureSuccessStatusCode();
                return null;
            }
        }
    }
}
