using System.Net.Http.Headers;
using System.Text;
using ActualChat.Users.Module;

namespace ActualChat.Users.Phone.Internal;

public sealed class SMSToVerificationCodeSender(IServiceProvider services) : IVerificationCodeSender, IDisposable
{
    private static readonly Uri SendUri = new("https://api.sms.to/sms/send");

    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();
    private IHttpClientFactory HttpClientFactory { get; } = services.HttpClientFactory();
    private ILogger Log { get; } = services.LogFor<SMSToVerificationCodeSender>();
    private HttpClient? _client;
    private HttpClient Client => _client ??= HttpClientFactory.CreateClient(nameof(SMSToVerificationCodeSender));

    public async Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
    {
        var apiKey = UsersSettings.SMSToApiKey;
        var sender = UsersSettings.SMSToFrom;

        if (apiKey.IsNullOrWhiteSpace() || sender.IsNullOrWhiteSpace()) {
            Log.LogError("SMS.to is not configured properly. SMSToApiKey or SMSToFrom is missing");
            throw Errors.DeliveryFailed();
        }

        try {
            message = await message.Resolve().ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post, SendUri);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var payload = new {
                to = phone.E164Value,
                sender_id = sender,
                message = message.Text,
            };

            var json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                Log.LogError("SMS.to send failed with status {StatusCode}", (int)response.StatusCode);
                throw Errors.DeliveryFailed();
            }

            var result = await response.Content.ReadFromJsonAsync<SendResponse>().ConfigureAwait(false);
            if (result?.Success is null)
                throw Errors.DeliveryFailed();
            if (result.Success == false) {
                Log.LogWarning("SMS.to rejected verification SMS");

                return null;
            }
            if (result.MessageId.IsNullOrWhiteSpace())
                throw Errors.DeliveryFailed();

            Log.LogInformation("SMS.to accepted verification SMS {MessageId}", result.MessageId);

            return TotpChannel.Sms;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to send text message via SMS.to");
            throw Errors.DeliveryFailed();
        }
    }

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

    // Nested types

    private sealed record SendResponse(
        [property: JsonPropertyName("success")] bool? Success,
        [property: JsonPropertyName("message_id")] string? MessageId);
}
