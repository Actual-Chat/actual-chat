using System.Security.Cryptography;
using System.Text;
using ActualChat.Users.Controllers;
using ActualChat.Users.Module;
using ActualChat.Users.Phone;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace ActualChat.Users.UnitTests.Phone;

public class TwilioMessageStatusControllerTest
{
    private const string AttemptId = "abcdefghijklmnopqrstuvwxyz123456";
    private const string PublicUrl = "https://voxt.ai/api/webhooks/twilio/message-status";
    private const string AuthToken = "test-auth-token";

    [Fact]
    public async Task SignedCallbackShouldPersistAndReturnNoContentBehindProxy()
    {
        // arrange
        var (controller, statuses, _) = CreateController();
        controller.Request.Scheme = "http";
        controller.Request.Host = new HostString("internal-service");

        // act
        var result = await controller.Update(default);

        // assert
        result.Should().BeOfType<NoContentResult>();
        statuses.Updates.Should().ContainSingle().Which.Should().Be((AttemptId, "SMtest", "delivered", ""));
    }

    [Fact]
    public async Task InvalidSignatureShouldNeverPersist()
    {
        // arrange
        var (controller, statuses, _) = CreateController();
        controller.Request.Headers["X-Twilio-Signature"] = "invalid";

        // act
        var result = await controller.Update(default);

        // assert
        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(403);
        statuses.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangedAttemptIdShouldInvalidateSignature()
    {
        // arrange
        var (controller, statuses, _) = CreateController();
        controller.Request.QueryString = new QueryString($"?attemptId={new string('a', 32)}");

        // act
        var result = await controller.Update(default);

        // assert
        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(403);
        statuses.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task SignedCallbackForAnotherAccountShouldBeRejected()
    {
        // arrange
        var (controller, statuses, _) = CreateController("ACother");

        // act
        var result = await controller.Update(default);

        // assert
        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(403);
        statuses.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingCredentialsShouldReturnServiceUnavailable()
    {
        // arrange
        var (controller, statuses, settings) = CreateController();
        settings.TwilioAuthToken = "";

        // act
        var result = await controller.Update(default);

        // assert
        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(503);
        statuses.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task PersistenceFailureShouldPropagateForRetry()
    {
        // arrange
        var (controller, statuses, _) = CreateController();
        statuses.MustFail = true;

        // act
        var update = () => controller.Update(default);

        // assert
        await update.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("http://voxt.ai/api/webhooks/twilio/message-status")]
    [InlineData("https://voxt.ai/wrong-route")]
    [InlineData("https://voxt.ai/api/webhooks/twilio/message-status?foo=bar")]
    [InlineData("https://voxt.ai/api/webhooks/twilio/message-status#fragment")]
    public void UnsafeCallbackConfigurationShouldBeRejected(string url)
    {
        // arrange
        var (_, statuses, settings) = CreateController();
        settings.TwilioStatusCallbackUrl = url;

        // act
        var getUri = () => statuses.GetCallbackUri(AttemptId);

        // assert
        getUri.Should().Throw<InvalidOperationException>();
    }

    private static (TwilioMessageStatusController, FakeStatuses, UsersSettings) CreateController(
        string accountSid = "ACtest")
    {
        var settings = new UsersSettings {
            TwilioAccountSid = "ACtest",
            TwilioAuthToken = AuthToken,
            TwilioStatusCallbackUrl = PublicUrl,
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(settings);
        var statuses = new FakeStatuses(services.BuildServiceProvider());
        services.AddSingleton<TwilioMessageStatuses>(statuses);
        var controller = new TwilioMessageStatusController(services.BuildServiceProvider());
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.QueryString = new QueryString($"?attemptId={AttemptId}");
        var form = new Dictionary<string, StringValues> {
            ["AccountSid"] = accountSid,
            ["MessageSid"] = "SMtest",
            ["MessageStatus"] = "delivered",
        };
        context.Request.Form = new FormCollection(form);
        var signedText = PublicUrl + context.Request.QueryString.Value
            + string.Concat(form.OrderBy(x => x.Key).Select(x => x.Key + x.Value.ToString()));
        var signature = HMACSHA1.HashData(Encoding.UTF8.GetBytes(AuthToken), Encoding.UTF8.GetBytes(signedText));
        context.Request.Headers["X-Twilio-Signature"] = Convert.ToBase64String(signature);
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        return (controller, statuses, settings);
    }

    private sealed class FakeStatuses(IServiceProvider services) : TwilioMessageStatuses(services)
    {
        public bool MustFail { get; set; }
        public List<(string AttemptId, string Sid, string Status, string Error)> Updates { get; } = [];

        public override Task<bool> Update(
            string attemptId,
            string accountSid,
            string messageSid,
            string status,
            string? errorCode,
            CancellationToken cancellationToken = default)
        {
            if (MustFail)
                throw new InvalidOperationException("Redis unavailable");

            Updates.Add((attemptId, messageSid, status, errorCode ?? ""));

            return Task.FromResult(true);
        }
    }
}
