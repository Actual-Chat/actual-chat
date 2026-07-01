using System.Net.Http.Headers;
using System.Text;
using ActualChat.Users.Module;

namespace ActualChat.Users.Phone.Internal;

public sealed class SMSToTextMessageSender(IServiceProvider services) : ITextMessageSender, IDisposable
{
    private static readonly Uri SendUri = new("https://api.sms.to/sms/send");

    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();
    private IHttpClientFactory HttpClientFactory { get; } = services.HttpClientFactory();
    private ILogger Log { get; } = services.LogFor<SMSToTextMessageSender>();
    private HttpClient? _client;
    private HttpClient Client => _client ??= HttpClientFactory.CreateClient(nameof(SMSToTextMessageSender));

    public async Task Send(ActualChat.Phone phone, string text)
    {
        var apiKey = UsersSettings.SMSToApiKey;
        var sender = UsersSettings.SMSToFrom;

        if (apiKey.IsNullOrWhiteSpace() || sender.IsNullOrWhiteSpace()) {
            Log.LogError("SMS.to is not configured properly. SMSToApiKey or SMSToFrom is missing");
            throw StandardError.External("We couldn't deliver the message to the specified phone number.");
        }

        try {
            using var request = new HttpRequestMessage(HttpMethod.Post, SendUri);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var payload = new {
                to = phone.E164Value,
                sender_id = sender,
                message = text,
            };

            var json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Log.LogError("SMS.to send failed with status {StatusCode}. Body: {Body}", (int)response.StatusCode, content);
                throw StandardError.External("We couldn't deliver the message to the specified phone number.");
            }
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to send text message via SMS.to");
            throw StandardError.External("We couldn't deliver the message to the specified phone number.");
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
}
