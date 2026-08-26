using System.Diagnostics.Metrics;
using ActualChat.Diagnostics;
using ActualChat.Users.Module;
using ActualChat.Users.Phone;
using ActualChat.Users.Phone.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Users.UnitTests.Phone;

public class CompositeVerificationCodeSenderTest
{
    private static readonly ActualChat.Phone ArmenianPhone = ActualChat.Phone.Parse("374-11223344");
    private static readonly ActualChat.Phone RussianPhone = ActualChat.Phone.Parse("7-9001234567");
    private static readonly VerificationMessage TestMessage = new("123456", "Voxt: your code is 123456.");
    private static readonly VerificationMessage TelegramOnlyMessage
        = TestMessage with { OnlyChannel = TotpChannel.Telegram };

    [Fact]
    public async Task ShouldUseSmsWhenItAcceptsTheNumber()
    {
        // arrange
        var telegram = new FakeSender(TotpChannel.Telegram);
        var twilio = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender(telegram, null, twilio);

        // act
        var channel = await sender.Send(ArmenianPhone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Sms);
        twilio.SendCount.Should().Be(1);
        telegram.SendCount.Should().Be(0, "Telegram costs money and SMS already delivered");
    }

    [Fact]
    public async Task ShouldFallBackToTelegramWhenSmsDeclines()
    {
        // arrange
        var telegram = new FakeSender(TotpChannel.Telegram);
        var twilio = new FakeSender(null);
        var sender = CreateSender(telegram, null, twilio);

        // act
        var channel = await sender.Send(ArmenianPhone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Telegram);
        twilio.SendCount.Should().Be(1);
        telegram.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task ShouldFallBackToTelegramWhenSmsThrows()
    {
        // arrange
        var telegram = new FakeSender(TotpChannel.Telegram);
        var twilio = new FakeSender(TotpChannel.Sms) { Error = new InvalidOperationException("provider down") };
        var sender = CreateSender(telegram, null, twilio);

        // act
        var channel = await sender.Send(ArmenianPhone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Telegram);
        twilio.SendCount.Should().Be(1);
        telegram.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task ShouldSkipSmsWhenOnlyTelegramIsAllowed()
    {
        // arrange
        var telegram = new FakeSender(TotpChannel.Telegram);
        var twilio = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender(telegram, null, twilio);

        // act
        var channel = await sender.Send(ArmenianPhone, TelegramOnlyMessage);

        // assert
        channel.Should().Be(TotpChannel.Telegram);
        twilio.SendCount.Should().Be(0, "a blocked prefix must not reach the SMS provider at all");
        telegram.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task ShouldUseTelegramWhenNoSmsSenderIsConfigured()
    {
        // arrange
        var telegram = new FakeSender(TotpChannel.Telegram);
        var sender = CreateSender(telegram, null, null);

        // act
        var channel = await sender.Send(ArmenianPhone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Telegram);
        telegram.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task ShouldRouteRussianNumbersThroughSmsTo()
    {
        // arrange
        var smsTo = new FakeSender(TotpChannel.Sms);
        var twilio = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender(null, smsTo, twilio);

        // act
        await sender.Send(RussianPhone, TestMessage);

        // assert
        smsTo.SendCount.Should().Be(1);
        twilio.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task ShouldDeliverThroughLogOnlyWhenItIsTheOnlySmsSender()
    {
        // arrange
        var telegram = new FakeSender(TotpChannel.Telegram);
        var logOnly = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender(telegram, null, null, logOnly);

        // act
        var channel = await sender.Send(ArmenianPhone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Sms);
        logOnly.SendCount.Should().Be(1);
        telegram.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task ShouldThrowWhenEveryChannelDeclines()
    {
        // arrange
        var telegram = new FakeSender(null);
        var twilio = new FakeSender(null);
        var sender = CreateSender(telegram, null, twilio);

        // act
        var send = () => sender.Send(ArmenianPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
    }

    [Fact]
    public async Task ShouldThrowWhenSmsDeclinesAndTelegramIsUnavailable()
    {
        // arrange
        var twilio = new FakeSender(null);
        var sender = CreateSender(null, null, twilio);

        // act
        var send = () => sender.Send(ArmenianPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
    }

    [Fact]
    public async Task ShouldNeverCallTelegramForAMatchingSkipPrefix()
    {
        // arrange
        var telegram = new FakeSender(TotpChannel.Telegram);
        var twilio = new FakeSender(null);
        var sender = CreateSender(telegram, null, twilio, skipTelegramPhonePrefixes: "374-");

        // act
        var send = () => sender.Send(ArmenianPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        telegram.SendCount.Should().Be(0, "a billed checkSendAbility must not happen for a skipped prefix");
    }

    [Fact]
    public async Task ShouldStillUseTelegramWhenSkipPrefixDoesNotMatch()
    {
        // arrange
        var telegram = new FakeSender(TotpChannel.Telegram);
        var twilio = new FakeSender(null);
        var sender = CreateSender(telegram, null, twilio, skipTelegramPhonePrefixes: "7-");

        // act
        var channel = await sender.Send(ArmenianPhone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Telegram);
        telegram.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task ShouldEmitChannelAndCountryTaggedMetrics()
    {
        // arrange
        var measurements = Collect();
        using var listener = StartListener(measurements);
        var twilio = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender(null, null, twilio);

        // act
        await sender.Send(ArmenianPhone, TestMessage);

        // assert
        measurements.Should().Contain(m
            => m.Name == "app.verification_code.sent" && m.Channel == "Sms" && m.Country == "374");
    }

    [Fact]
    public async Task ShouldClampUnknownCountryCodeToOther()
    {
        // arrange
        var measurements = Collect();
        using var listener = StartListener(measurements);
        var twilio = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender(null, null, twilio);
        var unknownCountryPhone = ActualChat.Phone.Parse("999-11223344");

        // act
        await sender.Send(unknownCountryPhone, TestMessage);

        // assert
        measurements.Should().Contain(m
            => m.Name == "app.verification_code.sent" && m.Country == "other");
    }

    [Fact]
    public async Task ShouldTagChannelSkippedWithFailedReasonWhenSmsThrows()
    {
        // arrange
        var measurements = Collect();
        using var listener = StartListener(measurements);
        var telegram = new FakeSender(TotpChannel.Telegram);
        var twilio = new FakeSender(TotpChannel.Sms) { Error = new InvalidOperationException("provider down") };
        var sender = CreateSender(telegram, null, twilio);

        // act
        await sender.Send(ArmenianPhone, TestMessage);

        // assert
        measurements.Should().Contain(m
            => m.Name == "app.verification_code.channel_skipped" && m.Channel == "Sms" && m.Reason == "failed");
    }

    [Fact]
    public async Task ShouldTagSmsSkippedWithBlockedReasonWhenOnlyTelegramIsAllowed()
    {
        // arrange
        var measurements = Collect();
        using var listener = StartListener(measurements);
        var telegram = new FakeSender(TotpChannel.Telegram);
        var twilio = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender(telegram, null, twilio);

        // act
        await sender.Send(ArmenianPhone, TelegramOnlyMessage);

        // assert
        measurements.Should().Contain(m
            => m.Name == "app.verification_code.channel_skipped"
                && m.Channel == "Sms" && m.Country == "374" && m.Reason == "blocked");
    }

    [Fact]
    public async Task ShouldTagTelegramSkippedWithUnconfiguredReasonWhenItIsMissing()
    {
        // arrange
        var measurements = Collect();
        using var listener = StartListener(measurements);
        var twilio = new FakeSender(null);
        var sender = CreateSender(null, null, twilio);

        // act
        var send = () => sender.Send(ArmenianPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        measurements.Should().Contain(m
                => m.Name == "app.verification_code.channel_skipped"
                    && m.Channel == "Telegram" && m.Reason == "unconfigured",
            "a missing channel and a deliberately skipped one are different operational problems");
    }

    [Fact]
    public async Task ShouldTagTelegramSkippedWithPrefixReasonWhenSkipRuleFires()
    {
        // arrange
        var measurements = Collect();
        using var listener = StartListener(measurements);
        var telegram = new FakeSender(TotpChannel.Telegram);
        var twilio = new FakeSender(null);
        var sender = CreateSender(telegram, null, twilio, skipTelegramPhonePrefixes: "374-");

        // act
        var send = () => sender.Send(ArmenianPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        measurements.Should().Contain(m
            => m.Name == "app.verification_code.channel_skipped"
                && m.Channel == "Telegram" && m.Reason == "prefix");
    }

    // Private methods

    private static List<(string Name, string? Channel, string? Country, string? Reason)> Collect() => new();

    private static MeterListener StartListener(
        List<(string Name, string? Channel, string? Country, string? Reason)> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => {
            if (instrument.Meter == AppInstruments.Meter)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => {
            var tagArray = tags.ToArray();
            var channel = tagArray.FirstOrDefault(t => t.Key == "channel").Value?.ToString();
            var country = tagArray.FirstOrDefault(t => t.Key == "country").Value?.ToString();
            var reason = tagArray.FirstOrDefault(t => t.Key == "reason").Value?.ToString();
            measurements.Add((instrument.Name, channel, country, reason));
        });
        listener.Start();

        return listener;
    }

    private static CompositeVerificationCodeSender CreateSender(
        FakeSender? telegram,
        FakeSender? smsTo,
        FakeSender? twilio,
        FakeSender? logOnly = null,
        string skipTelegramPhonePrefixes = "")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings { SkipTelegramPhonePrefixes = skipTelegramPhonePrefixes });
        if (telegram is not null)
            services.AddKeyedSingleton<IVerificationCodeSender>("Telegram", telegram);
        if (smsTo is not null)
            services.AddKeyedSingleton<IVerificationCodeSender>("SMSTo", smsTo);
        if (twilio is not null)
            services.AddKeyedSingleton<IVerificationCodeSender>("Twilio", twilio);
        if (logOnly is not null)
            services.AddKeyedSingleton<IVerificationCodeSender>("LogOnly", logOnly);

        return new CompositeVerificationCodeSender(services.BuildServiceProvider());
    }

    // Nested types

    private sealed class FakeSender(TotpChannel? result) : IVerificationCodeSender
    {
        public int SendCount { get; private set; }
        public Exception? Error { get; init; }

        public Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
        {
            SendCount++;
            if (Error is not null)
                throw Error;

            return Task.FromResult(result);
        }
    }
}
