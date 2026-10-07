using System.Net;
using ActualChat.Users.Module;
using ActualChat.Users.Phone;
using ActualChat.Users.Phone.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Users.UnitTests.Phone;

public class SMSToVerificationCodeSenderTest
{
    [Fact]
    public async Task AcceptedMessageShouldReturnSmsAndUseDocumentedRequestFields()
    {
        // arrange
        var handler = new FakeHandler(HttpStatusCode.OK,
            """{"success":true,"message_id":"11ec-832f-a6f3fcfe-9fea-02420a0002ab"}""");
        using var sender = CreateSender(handler);

        // act
        var channel = await sender.Send(ActualChat.Phone.Parse("7-9001234567"), new("123456", "test code"));

        // assert
        channel.Should().Be(TotpChannel.Sms);
        handler.SendCount.Should().Be(1);
        handler.Authorization.Should().Be("Bearer test-api-key");
        using var body = JsonDocument.Parse(handler.RequestBody!);
        body.RootElement.GetProperty("to").GetString().Should().Be("+79001234567");
        body.RootElement.GetProperty("sender_id").GetString().Should().Be("SMSto");
        body.RootElement.GetProperty("message").GetString().Should().Be("test code");
        body.RootElement.TryGetProperty("callback_url", out _).Should().BeFalse(
            "callbacks must not be enabled without an agreed authentication mechanism");
    }

    [Fact]
    public async Task ApplicationLevelRejectionShouldDeclineEvenWithHttp200()
    {
        // arrange
        var handler = new FakeHandler(HttpStatusCode.OK, """{"success":false,"message":"Rejected"}""");
        using var sender = CreateSender(handler);

        // act
        var channel = await sender.Send(ActualChat.Phone.Parse("7-9001234567"), new("123456", "test code"));

        // assert
        channel.Should().BeNull();
        handler.SendCount.Should().Be(1);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not JSON")]
    [InlineData("{\"success\":true}")]
    [InlineData("{\"success\":true,\"message_id\":\"\"}")]
    [InlineData("{\"success\":true,\"message_id\":\"   \"}")]
    [InlineData("{\"success\":\"true\",\"message_id\":\"id\"}")]
    public async Task IncompleteOrMalformedResponseShouldNotReportSuccess(string body)
    {
        // arrange
        var handler = new FakeHandler(HttpStatusCode.OK, body);
        using var sender = CreateSender(handler);

        // act
        var send = () => sender.Send(ActualChat.Phone.Parse("7-9001234567"), new("123456", "test code"));

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        handler.SendCount.Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpFailureShouldNotReportSuccessOrRetry(HttpStatusCode status)
    {
        // arrange
        var handler = new FakeHandler(status, """{"success":true,"message_id":"id"}""");
        using var sender = CreateSender(handler);

        // act
        var send = () => sender.Send(ActualChat.Phone.Parse("7-9001234567"), new("123456", "test code"));

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        handler.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task TimeoutShouldNotRetry()
    {
        // arrange
        var handler = new FakeHandler(HttpStatusCode.OK, "") { MustTimeout = true };
        using var sender = CreateSender(handler);

        // act
        var send = () => sender.Send(ActualChat.Phone.Parse("7-9001234567"), new("123456", "test code"));

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        handler.SendCount.Should().Be(1);
    }

    private static SMSToVerificationCodeSender CreateSender(FakeHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings { SMSToApiKey = "test-api-key", SMSToFrom = "SMSto" });
        services.AddSingleton<IHttpClientFactory>(new FakeFactory(handler));

        return new SMSToVerificationCodeSender(services.BuildServiceProvider());
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int SendCount { get; private set; }
        public bool MustTimeout { get; init; }
        public string? RequestBody { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            if (MustTimeout)
                throw new TaskCanceledException("Timeout after provider may have accepted the SMS");

            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
