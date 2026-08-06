using System.Net;
using System.Security.Cryptography;
using System.Text;
using ActualChat.Notifications.Module;

namespace ActualChat.Notifications.IntegrationTests;

public class ApnsClientTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void CreateJwtProducesVerifiableES256Token()
    {
        // arrange
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportPkcs8PrivateKeyPem();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_780_000_000);

        // act
        var jwt = ApnsClient.CreateJwt(pem, "KEY123", "TEAM456", now);

        // assert
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3);
        var header = JsonSerializer.Deserialize<Dictionary<string, string>>(FromBase64Url(parts[0]))!;
        header["alg"].Should().Be("ES256");
        header["kid"].Should().Be("KEY123");
        var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(FromBase64Url(parts[1]))!;
        claims["iss"].GetString().Should().Be("TEAM456");
        claims["iat"].GetInt64().Should().Be(1_780_000_000);
        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = FromBase64UrlBytes(parts[2]);
        key.VerifyData(signingInput, signature, HashAlgorithmName.SHA256).Should().BeTrue();
    }

    [Fact]
    public void DeadTokenResponsesAreRecognized()
    {
        // act + assert
        ApnsClient.IsDeadTokenResponse(HttpStatusCode.Gone, """{"reason":"Unregistered"}""").Should().BeTrue();
        ApnsClient.IsDeadTokenResponse(HttpStatusCode.BadRequest, """{"reason":"BadDeviceToken"}""").Should().BeTrue();
        ApnsClient.IsDeadTokenResponse(HttpStatusCode.BadRequest, """{"reason":"BadTopic"}""").Should().BeFalse();
        ApnsClient.IsDeadTokenResponse(HttpStatusCode.InternalServerError, "").Should().BeFalse();
    }

    [Fact]
    public async Task SendPushToTalkWakeSendsCorrectRequest()
    {
        // arrange
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyPath = Path.Combine(Path.GetTempPath(), $"apns-test-{Guid.NewGuid():N}.p8");
        await File.WriteAllTextAsync(keyPath, key.ExportPkcs8PrivateKeyPem());
        try {
            var settings = new NotificationsSettings {
                ApplePushKeyId = "KEY123",
                ApplePushTeamId = "TEAM456",
                ApplePushBundleId = "chat.actual.app",
                ApplePushPrivateKeyPath = keyPath,
            };
            var handler = new RecordingHandler();
            var client = new ApnsClient(settings, new FakeHttpClientFactory(handler), null!, NullLogger<ApnsClient>.Instance);
            var chatId = ChatId.Parse("testchatid1234567890");
            var startedAt = Moment.EpochStart + TimeSpan.FromDays(20_000);

            // act
            await client.SendPushToTalkWake(chatId, startedAt, "My Chat", [new Symbol("aabbccdd")], CancellationToken.None);

            // assert
            var request = handler.Requests.Should().ContainSingle().Subject;
            request.RequestUri!.AbsolutePath.Should().Be("/3/device/aabbccdd");
            request.Headers.GetValues("apns-push-type").Single().Should().Be("pushtotalk");
            request.Headers.GetValues("apns-topic").Single().Should().Be("chat.actual.app.voip-ptt");
            request.Headers.GetValues("apns-priority").Single().Should().Be("10");
            request.Headers.GetValues("authorization").Single().Should().StartWith("bearer ");
            var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(handler.Bodies.Single())!;
            body["kind"].GetString().Should().Be("SpeechStarted");
            body["chatId"].GetString().Should().Be(chatId.Value);
            body["chatTitle"].GetString().Should().Be("My Chat");
            body["timestamp"].GetInt64().Should().Be((long)startedAt.EpochOffset.TotalMilliseconds);
        }
        finally {
            File.Delete(keyPath);
        }
    }

    [Fact]
    public async Task UnconfiguredClientSilentlySkips()
    {
        // arrange
        var handler = new RecordingHandler();
        var client = new ApnsClient(
            new NotificationsSettings(), new FakeHttpClientFactory(handler), null!, NullLogger<ApnsClient>.Instance);

        // act
        await client.SendPushToTalkWake(
            ChatId.Parse("testchatid1234567890"), Moment.EpochStart, "T", [new Symbol("x")], CancellationToken.None);

        // assert
        handler.Requests.Should().BeEmpty();
    }

    // Private methods

    private static string FromBase64Url(string s)
        => Encoding.UTF8.GetString(FromBase64UrlBytes(s));

    private static byte[] FromBase64UrlBytes(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }

    // Nested types

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.push.apple.com") };
    }
}
