using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ActualChat.Diagnostics;
using ActualChat.Resilience;
using ActualChat.Users.Module;
using ActualChat.Users.Phone;
using ActualChat.Users.Phone.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Users.UnitTests.Phone;

public class VerificationMetricsTest
{
    [Theory]
    [InlineData("twilio", "374-11223344", TotpChannel.Sms, "374")]
    [InlineData("smsto", "7-9001234567", TotpChannel.Sms, "7")]
    [InlineData("telegram", "84-581234567", TotpChannel.Telegram, "84")]
    public async Task AcceptedCodeShouldCountProviderChannelAndCountry(
        string provider, string phone, TotpChannel channel, string country)
    {
        // arrange
        var measurements = new ConcurrentQueue<Measurement>();
        using var listener = Listen(measurements);
        var sender = CreateSender(provider, new FakeSender(channel));

        // act
        await sender.Send(ActualChat.Phone.Parse(phone), new("123456", "code"));

        // assert
        measurements.Should().Contain(m => m.Name == "app.verification_code.sent"
            && m.Tags["provider"] == provider && m.Tags["channel"] == channel.ToString()
            && m.Tags["country"] == country);
        measurements.Should().AllSatisfy(m => m.Tags.Keys.Should().BeSubsetOf(
            ["provider", "channel", "country", "reason"]));
    }

    [Fact]
    public async Task SmsToRejectionShouldCountDeclinedWithoutSuccess()
    {
        // arrange
        var measurements = new ConcurrentQueue<Measurement>();
        using var listener = Listen(measurements);
        var sender = CreateSender("smsto", new FakeSender(null));

        // act
        var send = () => sender.Send(ActualChat.Phone.Parse("7-9001234567"), new("123456", "code"));
        await send.Should().ThrowAsync<ExternalError>();

        // assert
        measurements.Should().Contain(m => m.Name == "app.verification_code.channel_skipped"
            && m.Tags["provider"] == "smsto" && m.Tags["reason"] == "declined");
    }

    [Fact]
    public async Task SmsLimitShouldCountRateLimitedWithoutCallingProvider()
    {
        // arrange
        var measurements = new ConcurrentQueue<Measurement>();
        using var listener = Listen(measurements);
        var sms = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender("smsto", sms, new DeniedPolicy());

        // act
        var send = () => sender.Send(ActualChat.Phone.Parse("7-9001234567"), new("123456", "code"));
        await send.Should().ThrowAsync<RateLimitExceededException>();

        // assert
        sms.SendCount.Should().Be(0);
        measurements.Should().Contain(m => m.Name == "app.verification_code.channel_skipped"
            && m.Tags["provider"] == "smsto" && m.Tags["reason"] == "rate_limited");
    }

    private static CompositeVerificationCodeSender CreateSender(
        string provider, FakeSender sender, RateLimitPolicy? policy = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings());
        services.AddSingleton(policy ?? RateLimitPolicy.Unlimited);
        var key = provider switch {
            "twilio" => "Twilio",
            "smsto" => "SMSTo",
            _ => "Telegram",
        };
        services.AddKeyedSingleton<IVerificationCodeSender>(key, sender);

        return new CompositeVerificationCodeSender(services.BuildServiceProvider());
    }

    private static MeterListener Listen(ConcurrentQueue<Measurement> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => {
            if (instrument.Meter == AppInstruments.Meter && instrument.Name.StartsWith("app.verification_code."))
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => {
            var dictionary = tags.ToArray().ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "");
            measurements.Enqueue(new Measurement(instrument.Name, dictionary));
        });
        listener.Start();

        return listener;
    }

    private sealed record Measurement(string Name, Dictionary<string, string> Tags);

    private sealed class FakeSender(TotpChannel? channel) : IVerificationCodeSender
    {
        public int SendCount { get; private set; }

        public Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
        {
            SendCount++;

            return Task.FromResult(channel);
        }
    }

    private sealed class DeniedPolicy : RateLimitPolicy
    {
        public override ValueTask Check(
            string method,
            RateLimitClass rateLimitClass,
            ReadOnlySpan<RateLimitIdentity> identities,
            CancellationToken cancellationToken = default)
            => throw new RateLimitExceededException(TimeSpan.FromMinutes(15));
    }
}
