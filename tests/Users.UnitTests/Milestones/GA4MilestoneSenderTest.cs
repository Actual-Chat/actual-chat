using System.Net;
using System.Text.Json.Nodes;
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Milestones;

public sealed class GA4MilestoneSenderTest
{
    private static readonly Moment Now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly UserId UserId = UserId.Parse("ga4test0001");

    [Fact]
    public void MilestoneWithFreshAttributionAndClientShouldBeSent()
    {
        // arrange
        var (ga4MilestoneSender, _) = NewSender();
        var context = NewContext(reachedAt: Now - TimeSpan.FromHours(1), attributionAge: TimeSpan.FromDays(2));

        // act
        var check = ga4MilestoneSender.Check(context);

        // assert
        check.Kind.Should().Be(MilestoneCheckKind.Send);
    }

    [Fact]
    public void MilestoneWithoutAttributionShouldBeSkipped()
    {
        // arrange
        var (ga4MilestoneSender, _) = NewSender();
        var context = NewContext(reachedAt: Now) with { Attribution = null };

        // act
        var check = ga4MilestoneSender.Check(context);

        // assert
        check.Kind.Should().Be(MilestoneCheckKind.Skip);
    }

    [Fact]
    public void AttributionOutsideWindowShouldBeSkipped()
    {
        // arrange
        var (ga4MilestoneSender, _) = NewSender();
        var context = NewContext(reachedAt: Now, attributionAge: TimeSpan.FromDays(31));

        // act
        var check = ga4MilestoneSender.Check(context);

        // assert
        check.Kind.Should().Be(MilestoneCheckKind.Skip, "the window is 30 days");
    }

    [Fact]
    public void MilestoneOlderThan72HoursShouldBeSkipped()
    {
        // arrange
        var (ga4MilestoneSender, _) = NewSender();
        var context = NewContext(reachedAt: Now - TimeSpan.FromHours(73));

        // act
        var check = ga4MilestoneSender.Check(context);

        // assert
        check.Kind.Should().Be(MilestoneCheckKind.Skip, "GA4 can't backdate an event further");
    }

    [Fact]
    public void MissingClientIdShouldWaitButNotPastTheBackdatingLimit()
    {
        // arrange
        var (ga4MilestoneSender, _) = NewSender();
        var reachedAt = Now - TimeSpan.FromHours(71.5);
        var context = NewContext(reachedAt: reachedAt) with { Clients = ApiArray<UserAnalyticsClient>.Empty };

        // act
        var check = ga4MilestoneSender.Check(context);

        // assert
        check.Kind.Should().Be(MilestoneCheckKind.Wait);
        check.RetryAt.Should().Be(reachedAt + TimeSpan.FromHours(72));
    }

    [Fact]
    public async Task SendShouldPostTheMilestoneForTheLastSeenDevice()
    {
        // arrange
        var (ga4MilestoneSender, handler) = NewSender();
        var reachedAt = Now - TimeSpan.FromHours(1);
        var context = NewContext(reachedAt: reachedAt) with {
            Clients = ApiArray.New(
                new UserAnalyticsClient(UserId, AnalyticsClientKind.Android, "android-id", Now - TimeSpan.FromDays(1)),
                new UserAnalyticsClient(UserId, AnalyticsClientKind.Ios, "ios-id", Now)),
        };

        // act
        var outcome = await ga4MilestoneSender.Send(context, default);

        // assert
        outcome.Result.Should().Be(MilestoneSendResult.Sent);
        handler.RequestUri!.Query.Should().Contain("firebase_app_id=ios-app").And.Contain("api_secret=ios-secret");
        var body = JsonNode.Parse(handler.Body!)!;
        body["app_instance_id"]!.GetValue<string>().Should().Be("ios-id");
        body["timestamp_micros"]!.GetValue<long>().Should().Be(reachedAt.EpochOffsetTicks / 10);
        var ev = body["events"]![0]!;
        ev["name"]!.GetValue<string>().Should().Be("conversation");
        ev["params"]!["value"]!.GetValue<double>().Should().Be(22);
        body["consent"]!["ad_user_data"]!.GetValue<string>().Should().Be("GRANTED");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, MilestoneSendResult.Retry)]
    [InlineData(HttpStatusCode.TooManyRequests, MilestoneSendResult.Retry)]
    [InlineData(HttpStatusCode.BadRequest, MilestoneSendResult.Failed)]
    public async Task ErrorStatusShouldBeClassified(HttpStatusCode statusCode, MilestoneSendResult expected)
    {
        // arrange
        var (ga4MilestoneSender, handler) = NewSender();
        handler.StatusCode = statusCode;
        var context = NewContext(reachedAt: Now);

        // act
        var outcome = await ga4MilestoneSender.Send(context, default);

        // assert
        outcome.Result.Should().Be(expected);
    }

    // Private methods

    private static (GA4MilestoneSender, RecordingHandler) NewSender()
    {
        var handler = new RecordingHandler();
        var settings = new UsersSettings {
            GA4 = new GA4Settings {
                AndroidFirebaseAppId = "android-app",
                AndroidApiSecret = "android-secret",
                IosFirebaseAppId = "ios-app",
                IosApiSecret = "ios-secret",
            },
        };
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(settings);
        services.AddHttpClient(GA4MilestoneSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return (new GA4MilestoneSender(services.BuildServiceProvider()), handler);
    }

    private static MilestoneSendContext NewContext(Moment reachedAt, TimeSpan? attributionAge = null)
    {
        var attributionSeenAt = reachedAt - (attributionAge ?? TimeSpan.FromDays(1));
        var attribution = new UserAttribution(
            1, UserId, AttributionOrigin.PlayReferrer, new AttributionDetails { Campaign = "walkie" },
            attributionSeenAt, attributionSeenAt, true, true);
        var milestone = new UserMilestone(10, UserId, MilestoneKind.Conversation, reachedAt, attribution.Id);
        var clients = ApiArray.New(new UserAnalyticsClient(UserId, AnalyticsClientKind.Android, "android-id", Now));
        return new MilestoneSendContext(milestone, attribution, clients, Now);
    }

    // Nested types

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.NoContent;
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(StatusCode);
        }
    }
}
