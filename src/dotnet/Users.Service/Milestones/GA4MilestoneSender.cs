using System.Net;
using System.Text.Json.Nodes;
using ActualChat.Users.Module;

namespace ActualChat.Users;

/// <summary>
/// Sends milestones to GA4 through the Measurement Protocol, on behalf of the device the user was last seen on,
/// so GA4 credits them to the campaign that brought that device; GA4's key events then feed Google Ads.
/// </summary>
public sealed class GA4MilestoneSender(IServiceProvider services) : IMilestoneSender
{
    public const string HttpClientName = nameof(GA4MilestoneSender);
    // The Measurement Protocol accepts an event dated at most this long ago
    private static readonly TimeSpan MaxBackdating = TimeSpan.FromHours(72);
    private static readonly TimeSpan ClientIdWaitPeriod = TimeSpan.FromHours(1);

    private GA4Settings Settings { get; } = services.GetRequiredService<UsersSettings>().GA4;
    private IHttpClientFactory HttpClientFactory { get; } = services.GetRequiredService<IHttpClientFactory>();
    private ILogger Log { get; } = services.LogFor<GA4MilestoneSender>();

    public MilestoneTarget Target => MilestoneTarget.GA4;
    public bool IsEnabled => Enum.GetValues<AnalyticsClientKind>().Any(Settings.IsEnabled);

    public MilestoneCheck Check(MilestoneSendContext context)
    {
        var (milestone, attribution, clients, now) = context;
        if (attribution is null)
            return MilestoneCheck.Skip("No attribution to credit it to");
        if (milestone.ReachedAt - attribution.LastSeenAt > Settings.AttributionWindow)
            return MilestoneCheck.Skip($"The attribution was last seen more than {Settings.AttributionWindow} before");

        var expiresAt = milestone.ReachedAt + MaxBackdating;
        if (now >= expiresAt)
            return MilestoneCheck.Skip($"Older than the {MaxBackdating} GA4 accepts");
        if (GetClient(clients) is null)
            return MilestoneCheck.Wait("No GA4 client id yet", Moment.Min(now + ClientIdWaitPeriod, expiresAt));

        return MilestoneCheck.Send;
    }

    public async Task<MilestoneSendOutcome> Send(MilestoneSendContext context, CancellationToken cancellationToken)
    {
        var milestone = context.Milestone;
        var client = GetClient(context.Clients)!;
        var (url, body) = CreateRequest(milestone, client);
        try {
            using var content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            var httpClient = HttpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            var statusCode = response.StatusCode;
            if (response.IsSuccessStatusCode)
                return new(MilestoneSendResult.Sent);
            if (statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500)
                return new(MilestoneSendResult.Retry, $"HTTP {(int)statusCode}");

            Log.LogWarning("GA4 rejected milestone {Kind} of user '{UserId}': HTTP {StatusCode}",
                milestone.Kind, milestone.UserId, (int)statusCode);
            return new(MilestoneSendResult.Failed, $"HTTP {(int)statusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested) {
            return new(MilestoneSendResult.Retry, e.GetType().Name);
        }
    }

    // Private methods

    private UserAnalyticsClient? GetClient(ApiArray<UserAnalyticsClient> clients)
        => clients.Where(c => Settings.IsEnabled(c.Kind)).MaxBy(c => c.LastSeenAt);

    private (string Url, JsonObject Body) CreateRequest(UserMilestone milestone, UserAnalyticsClient client)
    {
        var (streamKey, streamId, apiSecret, clientKey) = client.Kind switch {
            AnalyticsClientKind.Android => ("firebase_app_id", Settings.AndroidFirebaseAppId, Settings.AndroidApiSecret,
                "app_instance_id"),
            AnalyticsClientKind.Ios => ("firebase_app_id", Settings.IosFirebaseAppId, Settings.IosApiSecret,
                "app_instance_id"),
            _ => ("measurement_id", Settings.WebMeasurementId, Settings.WebApiSecret, "client_id"),
        };
        var url = $"{Settings.Endpoint}?{streamKey}={streamId.UrlEncode()}&api_secret={apiSecret.UrlEncode()}";

        var parameters = new JsonObject { ["engagement_time_msec"] = 1 };
        if (milestone.Kind.GetValue() is { } value) {
            parameters["value"] = value;
            parameters["currency"] = "USD";
        }
        var body = new JsonObject {
            [clientKey] = client.ClientId,
            ["timestamp_micros"] = milestone.ReachedAt.EpochOffsetTicks / 10,
            // The user agreed to data collection, which covers measurement; it is not consent to ad personalization
            ["consent"] = new JsonObject {
                ["ad_user_data"] = "GRANTED",
                ["ad_personalization"] = "DENIED",
            },
            ["events"] = new JsonArray(new JsonObject {
                ["name"] = milestone.Kind.GetEventName(),
                ["params"] = parameters,
            }),
        };
        return (url, body);
    }
}
