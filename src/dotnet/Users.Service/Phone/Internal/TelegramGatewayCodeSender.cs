using System.Net.Http.Headers;
using System.Text;
using ActualChat.Users.Module;

namespace ActualChat.Users.Phone.Internal;

public sealed class TelegramGatewayCodeSender(IServiceProvider services) : IVerificationCodeSender, IDisposable
{
    private static readonly Uri CheckSendAbilityUri = new("https://gatewayapi.telegram.org/checkSendAbility");
    private static readonly Uri SendVerificationMessageUri
        = new("https://gatewayapi.telegram.org/sendVerificationMessage");
    private static readonly HashSet<string> OperationalFailures = ["BALANCE_NOT_ENOUGH", "ACCESS_TOKEN_INVALID"];

    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();
    private IHttpClientFactory HttpClientFactory { get; } = services.HttpClientFactory();
    private ILogger Log { get; } = services.LogFor<TelegramGatewayCodeSender>();
    private HttpClient? _client;
    private HttpClient Client => _client ??= HttpClientFactory.CreateClient(nameof(TelegramGatewayCodeSender));

    public void Dispose()
    {
        try {
            _client?.Dispose();
        }
        catch {
            // ignore dispose exceptions
        }
        finally {
            _client = null;
        }
    }

    public async Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
    {
        // checkSendAbility is free of charge when the number can't receive a Telegram message, and the
        // matching sendVerificationMessage is free when it reuses the returned request_id - so the
        // two calls must stay adjacent, with nothing in between that can abort the send.
        var requestId = await CheckSendAbility(phone).ConfigureAwait(false);
        if (requestId is null)
            return null;

        var ttl = (int)UsersSettings.TelegramGatewayMessageTtl.TotalSeconds;
        var payload = new {
            phone_number = phone.E164Value,
            request_id = requestId,
            code = message.Code,
            ttl,
        };
        var response = await Post<TelegramGatewayResponse>(SendVerificationMessageUri, payload).ConfigureAwait(false);
        // Every Gateway response carries ok, and an application-level failure arrives as HTTP 200 with
        // ok:false - the status code alone would report an undelivered code as a successful send
        if (!response.Ok) {
            Log.LogError(
                "Telegram Gateway call to {Uri} reported a failure: {Error}",
                SendVerificationMessageUri, response.Error ?? "unknown error");

            throw Errors.DeliveryFailed();
        }

        return TotpChannel.Telegram;
    }

    // Private methods

    private async Task<string?> CheckSendAbility(ActualChat.Phone phone)
    {
        var payload = new { phone_number = phone.E164Value };
        var response = await Post<TelegramCheckSendAbilityResponse>(CheckSendAbilityUri, payload).ConfigureAwait(false);
        if (!response.Ok) {
            var error = response.Error;
            if (error is not null && OperationalFailures.Contains(error)) {
                Log.LogError(
                    "Telegram Gateway checkSendAbility for {Uri} failed: {Error}", CheckSendAbilityUri, error);

                throw Errors.DeliveryFailed();
            }

            Log.LogWarning(
                "Telegram Gateway checkSendAbility for {Uri} declined: {Error}",
                CheckSendAbilityUri, error ?? "unknown");

            return null;
        }

        var requestId = response.Result?.RequestId;
        if (requestId.IsNullOrEmpty()) {
            Log.LogWarning(
                "Telegram Gateway checkSendAbility for {Uri} was billed but returned no request_id",
                CheckSendAbilityUri);

            return null;
        }

        return requestId;
    }

    private async Task<TResponse> Post<TResponse>(Uri uri, object payload)
    {
        var token = UsersSettings.TelegramGatewayToken;
        if (token.IsNullOrWhiteSpace()) {
            Log.LogError("Telegram Gateway is not configured properly: TelegramGatewayToken is missing");

            throw Errors.DeliveryFailed();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try {
            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            await EnsureSuccess(response, uri).ConfigureAwait(false);
            var result = await response.Content.ReadFromJsonAsync<TResponse>().ConfigureAwait(false);
            if (result is null) {
                Log.LogError("Telegram Gateway call to {Uri} returned an empty response", uri);

                throw Errors.DeliveryFailed();
            }

            return result;
        }
        catch (Exception e) when (e is not ExternalError) {
            Log.LogError(e, "Telegram Gateway call to {Uri} failed", uri);

            throw Errors.DeliveryFailed(e);
        }
    }

    private async Task EnsureSuccess(HttpResponseMessage response, Uri uri)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Log.LogError(
            "Telegram Gateway call to {Uri} failed with status {StatusCode}. Body: {Body}",
            uri, (int)response.StatusCode, body);

        throw Errors.DeliveryFailed();
    }

    // Nested types

    private sealed record TelegramGatewayResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record TelegramCheckSendAbilityResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("result")] TelegramCheckSendAbilityResult? Result);

    private sealed record TelegramCheckSendAbilityResult(
        [property: JsonPropertyName("request_id")] string? RequestId);
}
