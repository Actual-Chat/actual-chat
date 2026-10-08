using ActualChat.Resilience;
using ActualChat.Users.Module;
using ActualChat.Users.Phone;
using ActualChat.Users.Phone.Internal;
using Microsoft.Extensions.DependencyInjection;
using PhoneNumbers;

namespace ActualChat.Users.UnitTests.Phone;

public class CompositeVerificationCodeSenderTest
{
    private static readonly ActualChat.Phone TestPhone = ActualChat.Phone.Parse("374-11223344");
    private static readonly VerificationMessage TestMessage = new("123456", "Voxt: your code is 123456.");

    [Fact]
    public async Task TelegramShouldBePreferredWithoutChargingSmsBudget()
    {
        // arrange
        var telegram = new FakeSender(TotpChannel.Telegram);
        var sms = new FakeSender(TotpChannel.Sms);
        var policy = new FakePolicy { MustReject = true };
        var sender = CreateSender(telegram, sms, policy);

        // act
        var channel = await sender.Send(TestPhone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Telegram);
        telegram.SendCount.Should().Be(1);
        sms.SendCount.Should().Be(0);
        policy.Classes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyDefiniteTelegramDeclineShouldFallBackToSms(bool mustThrow)
    {
        // arrange
        var telegram = new FakeSender(null) { MustThrow = mustThrow };
        var sms = new FakeSender(TotpChannel.Sms);
        var policy = new FakePolicy();
        var sender = CreateSender(telegram, sms, policy);
        var message = TestMessage with {
            Source = new RateLimitSource(new Session("test-session"), "203.0.113.1"),
        };

        // act
        if (mustThrow) {
            var send = () => sender.Send(TestPhone, message);
            await send.Should().ThrowAsync<ExternalError>();

            // assert
            sms.SendCount.Should().Be(0);
            policy.Classes.Should().BeEmpty();
            return;
        }

        var channel = await sender.Send(TestPhone, message);

        // assert
        channel.Should().Be(TotpChannel.Sms);
        sms.SendCount.Should().Be(1);
        policy.Classes.Should().Equal(RateLimitClass.SmsSend, RateLimitClass.SmsSendDaily);
        policy.Identities.Select(x => x.Kind).Should().Contain([
            RateLimitIdentityKind.Target, RateLimitIdentityKind.Session, RateLimitIdentityKind.IP,
        ]);
    }

    [Fact]
    public async Task SmsLimitShouldPreventProviderCall()
    {
        // arrange
        var sms = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender(null, sms, new FakePolicy { MustReject = true });

        // act
        var send = () => sender.Send(TestPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<RateLimitExceededException>();
        sms.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task DailySmsLimitShouldPreventProviderCall()
    {
        // arrange
        var sms = new FakeSender(TotpChannel.Sms);
        var policy = new FakePolicy { RejectClass = RateLimitClass.SmsSendDaily };
        var sender = CreateSender(null, sms, policy);

        // act
        var send = () => sender.Send(TestPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<RateLimitExceededException>();
        sms.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task BlockedSmsShouldNotBeUsedWhenTelegramDeclines()
    {
        // arrange
        var sms = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender(new FakeSender(null), sms);
        var message = TestMessage with { OnlyChannel = TotpChannel.Telegram };

        // act
        var send = () => sender.Send(TestPhone, message);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        sms.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task SkippedTelegramPrefixShouldFallBackToSms()
    {
        // arrange
        var telegram = new FakeSender(TotpChannel.Telegram);
        var sms = new FakeSender(TotpChannel.Sms);
        var sender = CreateSender(telegram, sms, skipPrefix: "374-");

        // act
        var channel = await sender.Send(TestPhone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Sms);
        telegram.SendCount.Should().Be(0);
        sms.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task DeclinedSmsShouldNotReportSuccess()
    {
        // arrange
        var sender = CreateSender(null, new FakeSender(null));

        // act
        var send = () => sender.Send(TestPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
    }

    [Fact]
    public async Task FailedSmsShouldNotReportSuccess()
    {
        // arrange
        var sender = CreateSender(null, new FakeSender(null) { MustThrow = true });

        // act
        var send = () => sender.Send(TestPhone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
    }

    [Fact]
    public void SmsBudgetsShouldBeTighterThanGeneralAuthBudgets()
    {
        // arrange
        var budgets = RateLimitBudgets.Default;

        // act
        var target = budgets.Get(RateLimitClass.SmsSend, RateLimitIdentityKind.Target);
        var session = budgets.Get(RateLimitClass.SmsSend, RateLimitIdentityKind.Session);
        var ip = budgets.Get(RateLimitClass.SmsSend, RateLimitIdentityKind.IP);
        var dailyTarget = budgets.Get(RateLimitClass.SmsSendDaily, RateLimitIdentityKind.Target);
        var dailySession = budgets.Get(RateLimitClass.SmsSendDaily, RateLimitIdentityKind.Session);
        var dailyIp = budgets.Get(RateLimitClass.SmsSendDaily, RateLimitIdentityKind.IP);

        // assert
        target.Should().Be(new SlidingWindowBudget(3, TimeSpan.FromMinutes(15)));
        session.Should().Be(new SlidingWindowBudget(3, TimeSpan.FromMinutes(15)));
        ip.Should().Be(new SlidingWindowBudget(20, TimeSpan.FromMinutes(15)));
        dailyTarget.Should().Be(new SlidingWindowBudget(5, TimeSpan.FromDays(1)));
        dailySession.Should().Be(new SlidingWindowBudget(10, TimeSpan.FromDays(1)));
        dailyIp.Should().Be(new SlidingWindowBudget(100, TimeSpan.FromDays(1)));
    }

    [Fact]
    public async Task RussianNumbersShouldStillUseSmsToWhenTelegramIsUnavailable()
    {
        // arrange
        var smsTo = new FakeSender(TotpChannel.Sms);
        var twilio = new FakeSender(TotpChannel.Sms);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings());
        services.AddSingleton(RateLimitPolicy.Unlimited);
        services.AddKeyedSingleton<IVerificationCodeSender>("SMSTo", smsTo);
        services.AddKeyedSingleton<IVerificationCodeSender>("Twilio", twilio);
        var sender = new CompositeVerificationCodeSender(services.BuildServiceProvider());

        // act
        var channel = await sender.Send(ActualChat.Phone.Parse("7-9001234567"), TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Sms);
        smsTo.SendCount.Should().Be(1);
        twilio.SendCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SmsToFailureShouldNotRetryThroughTwilio(bool mustThrow)
    {
        // arrange
        var smsTo = new FakeSender(null) { MustThrow = mustThrow };
        var twilio = new FakeSender(TotpChannel.Sms);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings());
        services.AddSingleton(RateLimitPolicy.Unlimited);
        services.AddKeyedSingleton<IVerificationCodeSender>("SMSTo", smsTo);
        services.AddKeyedSingleton<IVerificationCodeSender>("Twilio", twilio);
        var sender = new CompositeVerificationCodeSender(services.BuildServiceProvider());

        // act
        var send = () => sender.Send(ActualChat.Phone.Parse("7-9001234567"), TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        smsTo.SendCount.Should().Be(1);
        twilio.SendCount.Should().Be(0, "a provider failure may occur after acceptance and must not double-send");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("GB")]
    [InlineData("IN")]
    public async Task CheapTwilioDestinationShouldTrySmsBeforeTelegram(string region)
    {
        // arrange
        var sms = new FakeSender(TotpChannel.Sms);
        var telegram = new FakeSender(TotpChannel.Telegram);
        var policy = new FakePolicy();
        var sender = CreateSender(telegram, sms, policy);
        var phone = PhoneNumberUtil.GetInstance().GetExampleNumber(region).ToPhone();

        // act
        var channel = await sender.Send(phone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Sms);
        sms.SendCount.Should().Be(1);
        telegram.SendCount.Should().Be(0);
        policy.Classes.Should().Equal(RateLimitClass.SmsSend, RateLimitClass.SmsSendDaily);
    }

    [Fact]
    public async Task DefiniteSmsDeclineShouldFallBackToTelegram()
    {
        // arrange
        var sms = new FakeSender(null);
        var telegram = new FakeSender(TotpChannel.Telegram);
        var sender = CreateSender(telegram, sms);
        var phone = PhoneNumberUtil.GetInstance().GetExampleNumber("US").ToPhone();

        // act
        var channel = await sender.Send(phone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Telegram);
        sms.SendCount.Should().Be(1);
        telegram.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task AmbiguousSmsFailureShouldNotFallBackToTelegram()
    {
        // arrange
        var sms = new FakeSender(null) { MustThrow = true };
        var telegram = new FakeSender(TotpChannel.Telegram);
        var sender = CreateSender(telegram, sms);
        var phone = PhoneNumberUtil.GetInstance().GetExampleNumber("US").ToPhone();

        // act
        var send = () => sender.Send(phone, TestMessage);

        // assert
        await send.Should().ThrowAsync<ExternalError>();
        sms.SendCount.Should().Be(1);
        telegram.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task TelegramOnlyRestrictionShouldOverrideCheapSmsPrice()
    {
        // arrange
        var sms = new FakeSender(TotpChannel.Sms);
        var telegram = new FakeSender(TotpChannel.Telegram);
        var policy = new FakePolicy { MustReject = true };
        var sender = CreateSender(telegram, sms, policy);
        var phone = PhoneNumberUtil.GetInstance().GetExampleNumber("US").ToPhone();

        // act
        var channel = await sender.Send(phone, TestMessage with { OnlyChannel = TotpChannel.Telegram });

        // assert
        channel.Should().Be(TotpChannel.Telegram);
        sms.SendCount.Should().Be(0);
        policy.Classes.Should().BeEmpty();
    }

    [Fact]
    public async Task SmsToShouldPreferTelegramEvenForCheapTwilioDestinations()
    {
        // arrange
        var sms = new FakeSender(TotpChannel.Sms);
        var telegram = new FakeSender(TotpChannel.Telegram);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings());
        services.AddSingleton(RateLimitPolicy.Unlimited);
        services.AddKeyedSingleton<IVerificationCodeSender>("SMSTo", sms);
        services.AddKeyedSingleton<IVerificationCodeSender>("Telegram", telegram);
        var sender = new CompositeVerificationCodeSender(services.BuildServiceProvider());
        var phone = PhoneNumberUtil.GetInstance().GetExampleNumber("US").ToPhone();

        // act
        var channel = await sender.Send(phone, TestMessage);

        // assert
        channel.Should().Be(TotpChannel.Telegram);
        sms.SendCount.Should().Be(0);
        telegram.SendCount.Should().Be(1);
    }

    private static CompositeVerificationCodeSender CreateSender(
        FakeSender? telegram,
        FakeSender sms,
        RateLimitPolicy? policy = null,
        string skipPrefix = "")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new UsersSettings { SkipTelegramPhonePrefixes = skipPrefix });
        services.AddSingleton(policy ?? RateLimitPolicy.Unlimited);
        if (telegram is not null)
            services.AddKeyedSingleton<IVerificationCodeSender>("Telegram", telegram);
        services.AddKeyedSingleton<IVerificationCodeSender>("Twilio", sms);

        return new CompositeVerificationCodeSender(services.BuildServiceProvider());
    }

    private sealed class FakeSender(TotpChannel? result) : IVerificationCodeSender
    {
        public int SendCount { get; private set; }
        public bool MustThrow { get; init; }

        public Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
        {
            SendCount++;
            if (MustThrow)
                throw new InvalidOperationException("provider down");

            return Task.FromResult(result);
        }
    }

    private sealed class FakePolicy : RateLimitPolicy
    {
        public bool MustReject { get; init; }
        public RateLimitClass? RejectClass { get; init; }
        public List<RateLimitClass> Classes { get; } = [];
        public List<RateLimitIdentity> Identities { get; } = [];

        public override ValueTask Check(
            string method,
            RateLimitClass rateLimitClass,
            ReadOnlySpan<RateLimitIdentity> identities,
            CancellationToken cancellationToken = default)
        {
            Classes.Add(rateLimitClass);
            Identities.AddRange(identities.ToArray());
            if (MustReject || RejectClass == rateLimitClass)
                throw new RateLimitExceededException(TimeSpan.FromMinutes(15));

            return default;
        }
    }
}
