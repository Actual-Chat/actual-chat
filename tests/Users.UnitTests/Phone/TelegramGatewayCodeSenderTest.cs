using System.Net;
using System.Text;
using ActualChat.Users.Module;
using ActualChat.Users.Phone;
using ActualChat.Users.Phone.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Users.UnitTests.Phone;

public class TelegramGatewayCodeSenderTest
{
    private static readonly ActualChat.Phone TestPhone = ActualChat.Phone.Parse("374-11223344");
    private static readonly VerificationMessage TestMessage = new("123456", "Voxt: your code is 123456.");

    [Fact]
    public async Task SendShouldReturnTelegramWhenGatewayAccepts()
    {
        // arrange
        var handler = new FakeHandler([
            Ok("""{"ok":true,"result":{"request_id":"req-1"}}"""),
            Ok("""{"ok":true,"result":{"request_id":"req-1"}}"""),
        ]);
        var sender = CreateSender(handler);

        // act
        var channel = await sender.Send(TestPhone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Telegram);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Url.Should().EndWith("/checkSendAbility");
        GetProperty(handler.Requests[0].Body, "phone_number").Should().Be("+37411223344");
        handler.Requests[1].Url.Should().EndWith("/sendVerificationMessage");
        GetProperty(handler.Requests[1].Body, "request_id").Should().Be("req-1");
        GetProperty(handler.Requests[1].Body, "code").Should().Be("123456");
    }

    [Fact]
    public async Task SendShouldReturnNullWhenNumberHasNoTelegram()
    {
        // arrange
        var handler = new FakeHandler([Ok("""{"ok":false}""")]);
        var sender = CreateSender(handler);

        // act
        var channel = await sender.Send(TestPhone, TestMessage);

        // assert
        channel.Should().BeNull();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendShouldThrowExternalErrorWhenCheckSendAbilityReportsOperationalFailure()
    {
        // arrange
        var handler = new FakeHandler([Ok("""{"ok":false,"error":"BALANCE_NOT_ENOUGH"}""")]);
        var sender = CreateSender(handler);

        // act
        var send = () => sender.Send(TestPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendShouldReturnNullWhenCheckSendAbilityDeclinesWithUnknownError()
    {
        // arrange
        var handler = new FakeHandler([Ok("""{"ok":false,"error":"SOMETHING_ELSE"}""")]);
        var sender = CreateSender(handler);

        // act
        var channel = await sender.Send(TestPhone, TestMessage);

        // assert
        channel.Should().BeNull();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendShouldThrowWhenGatewayReturnsError()
    {
        // arrange
        var handler = new FakeHandler([
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") },
        ]);
        var sender = CreateSender(handler);

        // act
        var send = () => sender.Send(TestPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task SendShouldThrowExternalErrorWhenTransportFails()
    {
        // arrange
        var handler = new FakeHandler([], new HttpRequestException("connection reset"));
        var sender = CreateSender(handler);

        // act
        var send = () => sender.Send(TestPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
    }

    [Fact]
    public async Task SendShouldThrowExternalErrorWhenOkIsNotABoolean()
    {
        // arrange
        var handler = new FakeHandler([Ok("""{"ok":"true"}""")]);
        var sender = CreateSender(handler);

        // act
        var send = () => sender.Send(TestPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendShouldThrowExternalErrorWhenGatewayReportsApplicationFailure()
    {
        // arrange
        var handler = new FakeHandler([
            Ok("""{"ok":true,"result":{"request_id":"req-1"}}"""),
            Ok("""{"ok":false,"error":"BALANCE_NOT_ENOUGH"}"""),
        ]);
        var sender = CreateSender(handler);

        // act
        var send = () => sender.Send(TestPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task SendShouldFallBackToCodeLifetimeForTtl()
    {
        // arrange
        var sender = CreateSender(AcceptingHandler(out var handler));

        // act
        await sender.Send(TestPhone, TestMessage);

        // assert
        GetIntProperty(handler.Requests[1].Body, "ttl").Should().Be(900);
    }

    [Fact]
    public async Task SendShouldUseConfiguredTtl()
    {
        // arrange
        var sender = CreateSender(AcceptingHandler(out var handler), TimeSpan.FromMinutes(5));

        // act
        await sender.Send(TestPhone, TestMessage);

        // assert
        GetIntProperty(handler.Requests[1].Body, "ttl").Should().Be(300);
    }

    [Fact]
    public async Task SendShouldClampTtlToWhatTheGatewayAccepts()
    {
        // arrange
        var tooShort = CreateSender(AcceptingHandler(out var shortHandler), TimeSpan.FromSeconds(5));
        var tooLong = CreateSender(AcceptingHandler(out var longHandler), TimeSpan.FromHours(3));

        // act
        await tooShort.Send(TestPhone, TestMessage);
        await tooLong.Send(TestPhone, TestMessage);

        // assert
        GetIntProperty(shortHandler.Requests[1].Body, "ttl").Should().Be(30);
        GetIntProperty(longHandler.Requests[1].Body, "ttl").Should().Be(3600);
    }

    // Private methods

    private static FakeHandler AcceptingHandler(out FakeHandler handler)
        => handler = new FakeHandler([
            Ok("""{"ok":true,"result":{"request_id":"req-1"}}"""),
            Ok("""{"ok":true}"""),
        ]);

    private static TelegramGatewayCodeSender CreateSender(FakeHandler handler, TimeSpan? gatewayTtl = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings {
            TelegramGatewayToken = "test-token",
            TelegramGatewayTtl = gatewayTtl,
        });
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(handler));

        return new TelegramGatewayCodeSender(services.BuildServiceProvider());
    }

    private static HttpResponseMessage Ok(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string? GetProperty(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(name).GetString();
    }

    private static int GetIntProperty(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(name).GetInt32();
    }

    // Nested types

    private sealed record CapturedRequest(string Url, string Body);

    private sealed class FakeHandler(IReadOnlyList<HttpResponseMessage> responses, Exception? throwOnSend = null)
        : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new CapturedRequest(request.RequestUri!.ToString(), body));
            if (throwOnSend is not null)
                throw throwOnSend;

            return responses[Requests.Count - 1];
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, false);
    }
}
