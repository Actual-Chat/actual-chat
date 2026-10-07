using System.Net;
using ActualChat.Users.Module;
using ActualChat.Users.Phone;
using ActualChat.Users.Phone.Internal;
using Microsoft.Extensions.DependencyInjection;
using Twilio.Clients;

namespace ActualChat.Users.UnitTests.Phone;

public class TwilioVerificationCodeSenderTest
{
    [Theory]
    [InlineData("accepted")]
    [InlineData("sending")]
    [InlineData("queued")]
    [InlineData("sent")]
    [InlineData("delivered")]
    public async Task AcceptedMessageShouldReturnSmsChannel(string status)
    {
        // arrange
        var sender = CreateSender(HttpStatusCode.Created,
            $$"""{"sid":"SMtest","status":"{{status}}","error_code":null}""");

        // act
        var channel = await sender.Send(ActualChat.Phone.Parse("374-11223344"), new("123456", "code"));

        // assert
        channel.Should().Be(TotpChannel.Sms);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("undelivered")]
    [InlineData("canceled")]
    [InlineData("unknown")]
    public async Task RejectedMessageShouldNotReportSuccess(string status)
    {
        // arrange
        var sender = CreateSender(HttpStatusCode.Created,
            $$"""{"sid":"SMtest","status":"{{status}}","error_code":null}""");

        // act
        var send = () => sender.Send(ActualChat.Phone.Parse("374-11223344"), new("123456", "code"));

        // assert
        await send.Should().ThrowAsync<ExternalError>();
    }

    [Fact]
    public async Task InvalidNumberShouldDeclineRatherThanReportSuccess()
    {
        // arrange
        var sender = CreateSender(HttpStatusCode.BadRequest,
            """{"code":21211,"message":"Invalid To number","status":400}""");

        // act
        var channel = await sender.Send(ActualChat.Phone.Parse("374-11223344"), new("123456", "code"));

        // assert
        channel.Should().BeNull();
    }

    [Theory]
    [InlineData("{\"sid\":\"\",\"status\":\"queued\"}")]
    [InlineData("{\"sid\":\"SMtest\",\"status\":\"queued\",\"error_code\":30003}")]
    [InlineData("{\"sid\":\"SMtest\"}")]
    public async Task IncompleteOrErroredResponseShouldNotReportSuccess(string body)
    {
        // arrange
        var sender = CreateSender(HttpStatusCode.Created, body);

        // act
        var send = () => sender.Send(ActualChat.Phone.Parse("374-11223344"), new("123456", "code"));

        // assert
        await send.Should().ThrowAsync<ExternalError>();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task AcceptedSendShouldSucceedEvenWhenTrackingFails(bool mustFailTracking, bool isMatched)
    {
        // arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings {
            TwilioAccountSid = "ACtest",
            TwilioSmsFrom = "+16209383270",
            TwilioAuthToken = "test-auth-token",
            TwilioStatusCallbackUrl = "https://voxt.ai/api/webhooks/twilio/message-status",
        });
        var statuses = new FakeStatuses(services.BuildServiceProvider()) {
            MustFailTracking = mustFailTracking,
            IsMatched = isMatched,
        };
        services.AddSingleton<TwilioMessageStatuses>(statuses);
        var handler = new FakeHandler(HttpStatusCode.Created, """{"sid":"SMtest","status":"queued"}""");
        var httpClient = new Twilio.Http.SystemNetHttpClient(new HttpClient(handler));
        services.AddSingleton<ITwilioRestClient>(new TwilioRestClient("ACtest", "secret", httpClient: httpClient));
        var sender = new TwilioVerificationCodeSender(services.BuildServiceProvider());

        // act
        var channel = await sender.Send(ActualChat.Phone.Parse("374-11223344"), new("123456", "code"));

        // assert
        channel.Should().Be(TotpChannel.Sms);
        statuses.AttemptId.Should().HaveLength(32);
        statuses.AttemptId.Should().MatchRegex("^[a-zA-Z0-9_-]{32}$");
        Uri.UnescapeDataString(handler.RequestBody!).Should().Contain(
            $"StatusCallback=https://voxt.ai/api/webhooks/twilio/message-status?attemptId={statuses.AttemptId}");
        statuses.MessageSid.Should().Be("SMtest");
    }

    private static TwilioVerificationCodeSender CreateSender(HttpStatusCode status, string body)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings { TwilioSmsFrom = "+16209383270" });
        var httpClient = new Twilio.Http.SystemNetHttpClient(new HttpClient(new FakeHandler(status, body)));
        services.AddSingleton<ITwilioRestClient>(new TwilioRestClient("ACtest", "secret", httpClient: httpClient));

        return new TwilioVerificationCodeSender(services.BuildServiceProvider());
    }

    private sealed class FakeStatuses(IServiceProvider services) : TwilioMessageStatuses(services)
    {
        public string? AttemptId { get; private set; }
        public string? MessageSid { get; private set; }
        public bool MustFailTracking { get; init; }
        public bool IsMatched { get; init; } = true;

        public override Task Begin(string attemptId, CancellationToken cancellationToken = default)
        {
            AttemptId = attemptId;

            return Task.CompletedTask;
        }

        public override Task<bool> Update(
            string attemptId,
            string accountSid,
            string messageSid,
            string status,
            string? errorCode,
            CancellationToken cancellationToken = default)
        {
            attemptId.Should().Be(AttemptId);
            MessageSid = messageSid;
            if (MustFailTracking)
                throw new InvalidOperationException("Redis unavailable after provider acceptance");

            return Task.FromResult(IsMatched);
        }
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
