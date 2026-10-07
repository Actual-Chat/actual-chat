using System.Net;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ActualChat.Diagnostics;
using ActualChat.Testing.Host;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualChat.Users.Phone;
using ActualLab.Redis;
using ActualLab.Generators;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class TwilioMessageStatusesTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task EarlyDeliveryCallbackShouldSurviveInitialQueuedResponseAndDuplicates()
    {
        // arrange
        var statuses = CreateStatuses();
        var attemptId = RandomStringGenerator.Default.Next(32);
        await statuses.Begin(attemptId);

        // act
        var isEarlyMatched = await statuses.Update(attemptId, "ACtest", "SMtest", "delivered", null);
        await statuses.Update(attemptId, "ACtest", "SMtest", "queued", null);
        await statuses.Update(attemptId, "ACtest", "SMtest", "sent", null);
        await statuses.Update(attemptId, "ACtest", "SMtest", "delivered", null);

        // assert
        isEarlyMatched.Should().BeTrue();
        (await statuses.GetStatus(attemptId)).Should().Be("delivered");
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("undelivered")]
    [InlineData("canceled")]
    public async Task TerminalFailureShouldSurviveDelayedEarlierStatuses(string terminalStatus)
    {
        // arrange
        var statuses = CreateStatuses();
        var attemptId = RandomStringGenerator.Default.Next(32);
        await statuses.Begin(attemptId);

        // act
        await statuses.Update(attemptId, "ACtest", "SMtest", terminalStatus, "30003");
        await statuses.Update(attemptId, "ACtest", "SMtest", "sent", null);
        await statuses.Update(attemptId, "ACtest", "SMtest", "queued", null);

        // assert
        (await statuses.GetStatus(attemptId)).Should().Be(terminalStatus);
    }

    [Fact]
    public async Task CallbackShouldNotRebindMessageOrAccount()
    {
        // arrange
        var statuses = CreateStatuses();
        var attemptId = RandomStringGenerator.Default.Next(32);
        await statuses.Begin(attemptId);
        await statuses.Update(attemptId, "ACtest", "SMtest", "queued", null);

        // act
        var isOtherMessageMatched = await statuses.Update(attemptId, "ACtest", "SMother", "delivered", null);
        var isOtherAccountMatched = await statuses.Update(attemptId, "ACother", "SMtest", "delivered", null);

        // assert
        isOtherMessageMatched.Should().BeFalse();
        isOtherAccountMatched.Should().BeFalse();
        (await statuses.GetStatus(attemptId)).Should().Be("queued");
    }

    [Fact]
    public async Task UnknownAttemptShouldNotCreateRedisState()
    {
        // arrange
        var statuses = CreateStatuses();
        var attemptId = RandomStringGenerator.Default.Next(32);

        // act
        var isMatched = await statuses.Update(attemptId, "ACtest", "SMtest", "delivered", null);

        // assert
        isMatched.Should().BeFalse();
        (await statuses.GetStatus(attemptId)).Should().BeNull();
    }

    [Fact]
    public async Task CallbackEndpointShouldBeRoutableWithoutUserLogin()
    {
        // arrange
        using var http = AppHost.NewHttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["MessageSid"] = "SMtest",
            ["MessageStatus"] = "delivered",
        });

        // act
        var response = await http.PostAsync($"/{TwilioMessageStatuses.Route}?attemptId={new string('a', 32)}", form);

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "the local host has no callback credentials, but the anonymous endpoint must be discovered");
    }

    [Fact]
    public async Task DuplicateCallbacksShouldCountDeliveryOnlyOnce()
    {
        // arrange
        var statuses = CreateStatuses();
        var attemptId = RandomStringGenerator.Default.Next(32);
        await statuses.Begin(attemptId);
        await statuses.Update(attemptId, "ACtest", "SMtest", "queued", null);
        var counts = new ConcurrentQueue<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => {
            if (instrument.Meter == AppInstruments.Meter
                && instrument.Name == "app.verification_code.delivery_status")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => {
            var values = tags.ToArray();
            if (values.Any(t => t.Key == "status" && Equals(t.Value, "delivered")))
                counts.Enqueue(value);
        });
        listener.Start();

        // act
        await statuses.Update(attemptId, "ACtest", "SMtest", "delivered", null);
        await statuses.Update(attemptId, "ACtest", "SMtest", "delivered", null);
        await statuses.Update(attemptId, "ACtest", "SMtest", "sent", null);

        // assert
        counts.Sum().Should().Be(1);
        (await statuses.GetStatus(attemptId)).Should().Be("delivered");
    }

    private TwilioMessageStatuses CreateStatuses()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings { TwilioAccountSid = "ACtest" });
        services.AddSingleton(AppHost.Services.GetRequiredService<RedisDb<UsersDbContext>>());

        return new TwilioMessageStatuses(services.BuildServiceProvider());
    }
}
