using System.Net.Http.Headers;
using System.Text;
using ActualChat.Chat.Db;
using ActualChat.Security;
using ActualChat.WebHooks;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

/// <summary>
/// Drives one hook's outbox: signs and POSTs its head-of-line delivery, records the result,
/// and tells <see cref="Flows.WebHookDeliveryFlow"/> whether to go on, wait, or stop.
/// </summary>
public sealed class WebHookDeliverer(IServiceProvider services)
{
    public const string HttpClientName = "WebHooks";
    private const string UserAgent = "Voxt-Hooks/1";
    private const int MaxErrorBodyLength = 4 * 1024;
    private static readonly Outcome Done = new(false, null);
    private static readonly Outcome More = new(true, null);

    private IServiceProvider Services { get; } = services;
    private DbHub<ChatDbContext> DbHub => field ??= Services.DbHub<ChatDbContext>();
    private WebHookSecrets Secrets => field ??= Services.GetRequiredService<WebHookSecrets>();
    private WebHookPayloads Payloads => field ??= Services.GetRequiredService<WebHookPayloads>();
    private EgressGuard EgressGuard => field ??= Services.GetRequiredService<EgressGuard>();
    private IHttpClientFactory HttpClientFactory => field ??= Services.GetRequiredService<IHttpClientFactory>();
    private ICommander Commander => field ??= Services.Commander();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private HostInfo HostInfo => field ??= Services.HostInfo();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<Outcome> DeliverNext(WebHookId hookId, CancellationToken cancellationToken)
    {
        var dbWebHook = await GetDbWebHook(hookId, cancellationToken).ConfigureAwait(false);
        if (dbWebHook is null)
            return Done;

        var dbDelivery = await GetHeadOfLine(hookId, cancellationToken).ConfigureAwait(false);
        if (dbDelivery is null || !dbWebHook.IsEnabled)
            return Done;

        var now = Clocks.SystemClock.Now;
        if (dbDelivery.NextAttemptAt is { } nextAttemptAt && nextAttemptAt.ToMoment() > now)
            return new Outcome(true, nextAttemptAt.ToMoment() - now);

        var attempt = await Send(dbWebHook, dbDelivery.Id, dbDelivery.Payload, cancellationToken)
            .ConfigureAwait(false);
        now = Clocks.SystemClock.Now; // The send may have taken the whole timeout
        var scopeId = dbWebHook.ScopeId;
        if (attempt.IsUnsafeUrl) {
            await Record(dbWebHook, dbDelivery.Id, WebHookDeliveryStatus.Failed, attempt, null, cancellationToken)
                .ConfigureAwait(false);
            await Disable(hookId, scopeId, WebHookDisabledReason.UnsafeUrl, attempt.Error, cancellationToken)
                .ConfigureAwait(false);
            return Done;
        }

        switch (attempt.StatusCode) {
        case >= 200 and < 300:
            await Record(dbWebHook, dbDelivery.Id, WebHookDeliveryStatus.Succeeded, attempt, null, cancellationToken)
                .ConfigureAwait(false);
            return More;
        case 410:
            await Record(dbWebHook, dbDelivery.Id, WebHookDeliveryStatus.Failed, attempt, null, cancellationToken)
                .ConfigureAwait(false);
            await Disable(hookId, scopeId, WebHookDisabledReason.DeliveryFailures, "410 Gone", cancellationToken)
                .ConfigureAwait(false);
            return Done;
        case null or 429 or >= 500:
            var retryDelays = Constants.WebHooks.RetryDelays;
            var retryIn = retryDelays[Math.Min(dbDelivery.Attempts, retryDelays.Length - 1)];
            await Record(
                    dbWebHook, dbDelivery.Id, WebHookDeliveryStatus.Pending, attempt, now + retryIn, cancellationToken)
                .ConfigureAwait(false);
            if (now - dbDelivery.CreatedAt.ToMoment() <= Constants.WebHooks.DisableAfter)
                return new Outcome(true, retryIn);

            // The line hasn't moved for too long: the receiver is gone for good, as far as we can tell
            await Disable(hookId, scopeId, WebHookDisabledReason.DeliveryFailures, attempt.Error, cancellationToken)
                .ConfigureAwait(false);
            return Done;
        default:
            await Record(dbWebHook, dbDelivery.Id, WebHookDeliveryStatus.Failed, attempt, null, cancellationToken)
                .ConfigureAwait(false);
            return More;
        }
    }

    public async Task<WebHookTestResult> SendPing(WebHook hook, string sentBy, CancellationToken cancellationToken)
    {
        var dbWebHook = await GetDbWebHook(hook.Id, cancellationToken).Require().ConfigureAwait(false);
        var payload = Payloads.Ping(hook, sentBy);
        var deliveryId = WebHookJson.EnvelopeId(payload);
        var attempt = await Send(dbWebHook, deliveryId, payload, cancellationToken).ConfigureAwait(false);
        var isSuccess = attempt.StatusCode is >= 200 and < 300;
        return new WebHookTestResult(isSuccess, attempt.StatusCode, attempt.Error, attempt.LatencyMs);
    }

    // Private methods

    private async Task<DbWebHookDelivery?> GetHeadOfLine(WebHookId hookId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        return await dbContext.WebHookDeliveries
            .Where(x => x.WebHookId == hookId.Value && x.Status == WebHookDeliveryStatus.Pending)
            .OrderBy(x => x.Seq)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private Task Record(
        DbWebHook dbWebHook,
        string deliveryId,
        WebHookDeliveryStatus status,
        Attempt attempt,
        Moment? nextAttemptAt,
        CancellationToken cancellationToken)
        => Commander.Call(
            new WebHooksBackend_RecordDelivery(
                WebHookId.Parse(dbWebHook.Id), dbWebHook.ScopeId, deliveryId,
                status, attempt.StatusCode, attempt.Error, attempt.LatencyMs, nextAttemptAt),
            cancellationToken);

    private Task Disable(
        WebHookId hookId,
        string scopeId,
        WebHookDisabledReason reason,
        string? error,
        CancellationToken cancellationToken)
        => Commander.Call(new WebHooksBackend_Disable(hookId, scopeId, reason, error), cancellationToken);

    private async Task<DbWebHook?> GetDbWebHook(WebHookId hookId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        return await dbContext.WebHooks
            .FirstOrDefaultAsync(x => x.Id == hookId.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Attempt> Send(
        DbWebHook dbWebHook,
        string deliveryId,
        string payload,
        CancellationToken cancellationToken)
    {
        // Re-asserted at delivery time, so a URL saved under looser rules is never posted to
        if (!WebHooksBackend.IsAllowedUrl(dbWebHook.Url, HostInfo)
            || !Uri.TryCreate(dbWebHook.Url, UriKind.Absolute, out var uri)
            || !await EgressGuard.IsAllowed(uri.DnsSafeHost, cancellationToken).ConfigureAwait(false))
            return new Attempt(null, "URL is not allowed", 0, IsUnsafeUrl: true);

        var now = Clocks.SystemClock.Now;
        var timestamp = now.ToIntegerUnixEpoch();
        var secrets = Secrets.GetSigningSecrets(dbWebHook, now);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.TryAddWithoutValidation("webhook-id", deliveryId);
        request.Headers.TryAddWithoutValidation("webhook-timestamp", timestamp.ToString());
        request.Headers.TryAddWithoutValidation(
            "webhook-signature", StandardWebhookSigner.SignatureHeader(deliveryId, timestamp, payload, secrets));
        request.Headers.TryAddWithoutValidation("user-agent", UserAgent);
        if (!dbWebHook.CustomHeaderName.IsNullOrEmpty() && Secrets.GetCustomHeaderValue(dbWebHook) is { } headerValue)
            request.Headers.TryAddWithoutValidation(dbWebHook.CustomHeaderName, headerValue);
        request.Content = new StringContent(payload, new MediaTypeHeaderValue("application/json"));

        var client = HttpClientFactory.CreateClient(HttpClientName);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Constants.WebHooks.DeliveryTimeout);
        var startedAt = CpuTimestamp.Now;
        try {
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;
            var latencyMs = LatencyMs(startedAt);
            if (response.IsSuccessStatusCode)
                return new Attempt(statusCode, null, latencyMs);

            var error = statusCode is >= 300 and < 400
                ? "redirect not followed"
                : await ReadError(response, timeoutCts.Token).ConfigureAwait(false);
            return new Attempt(statusCode, error, latencyMs);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            var timeout = Constants.WebHooks.DeliveryTimeout;
            return new Attempt(null, $"no response within {timeout.ToShortString()}", LatencyMs(startedAt));
        }
        catch (Exception e) when (e is HttpRequestException or IOException) {
            Log.LogDebug(e, "Delivery {DeliveryId} to {Host} failed", deliveryId, uri.DnsSafeHost);
            return new Attempt(null, e.Message, LatencyMs(startedAt));
        }
    }

    private static async Task<string> ReadError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var statusLine = $"{(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);

        var buffer = new byte[MaxErrorBodyLength];
        var length = await stream
            .ReadAtLeastAsync(buffer, MaxErrorBodyLength, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);
        var body = Encoding.UTF8.GetString(buffer, 0, length).Trim();
        return body.IsNullOrEmpty() ? statusLine : $"{statusLine}: {body}";
    }

    private static int LatencyMs(CpuTimestamp startedAt)
        => (int)Math.Min(startedAt.Elapsed.TotalMilliseconds, int.MaxValue);

    // Nested types

    public sealed record Outcome(bool HasMore, TimeSpan? RetryIn);

    private sealed record Attempt(int? StatusCode, string? Error, int LatencyMs, bool IsUnsafeUrl = false);
}
