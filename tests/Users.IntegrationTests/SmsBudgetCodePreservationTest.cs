using ActualChat.Resilience;
using ActualChat.Testing.Host;
using ActualChat.Users.Phone;
using ActualChat.Users.Phone.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class SmsBudgetCodePreservationTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(false, RateLimitClass.SmsSend)]
    [InlineData(true, RateLimitClass.SmsSend)]
    [InlineData(false, RateLimitClass.SmsSendDaily)]
    [InlineData(true, RateLimitClass.SmsSendDaily)]
    public async Task RejectedSmsShouldPreservePreviouslyDeliveredCode(bool isLegacy, RateLimitClass rejectedClass)
    {
        // arrange
        var sms = new FakeSender(TotpChannel.Sms);
        var telegram = new FakeSender(null);
        var policy = new DeniedSmsPolicy(rejectedClass);
        var hostSuffix = $"sms-budget-{Ulid.NewUlid().ToString().ToLower()}";
        await using var host = await NewAppHost(hostSuffix, options => options with {
            ConfigureServices = (_, services) => {
                services.Replace(ServiceDescriptor.Singleton<RateLimitPolicy>(policy));
                services.AddKeyedSingleton<IVerificationCodeSender>("Twilio", sms);
                services.AddKeyedSingleton<IVerificationCodeSender>("Telegram", telegram);
                services.Replace(
                    ServiceDescriptor.Singleton<IVerificationCodeSender, CompositeVerificationCodeSender>());
            },
        });
        await using var tester = host.NewWebClientTester(Out);
        var codes = host.Services.GetRequiredService<ITotpCodesBackend>();
        var phone = ActualChat.Phone.New("1", $"555{Random.Shared.Next(1_000_000, 9_999_999)}");
        var previousCode = await codes.Generate(phone.Value, TotpPurpose.SignInPhone);
        Func<Task> send = isLegacy
            ? async () => {
#pragma warning disable CS0618
                await tester.Commander.Call(new PhoneAuth_SendTotp {
                    Session = tester.Session, Phone = phone, Purpose = TotpPurpose.SignInPhone,
                });
#pragma warning restore CS0618
            }
            : async () => {
                await tester.Commander.Call(new PhoneAuth_SendCode {
                    Session = tester.Session, Phone = phone, Purpose = TotpPurpose.SignInPhone,
                });
            };

        // act
        await send.Should().ThrowAsync<RateLimitExceededException>();
        var isPreviousCodeValid = await codes.Validate(phone.Value, TotpPurpose.SignInPhone, previousCode);

        // assert
        isPreviousCodeValid.Should().BeTrue("a rejected resend must not replace the code the user already received");
        telegram.SendCount.Should().Be(1);
        sms.SendCount.Should().Be(0);
    }

    private sealed class FakeSender(TotpChannel? channel) : IVerificationCodeSender
    {
        public int SendCount { get; private set; }

        public Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
        {
            SendCount++;

            return Task.FromResult(channel);
        }
    }

    private sealed class DeniedSmsPolicy(RateLimitClass rejectedClass) : RateLimitPolicy
    {
        public override ValueTask Check(
            string method,
            RateLimitClass rateLimitClass,
            ReadOnlySpan<RateLimitIdentity> identities,
            CancellationToken cancellationToken = default)
        {
            if (rateLimitClass == rejectedClass)
                throw new RateLimitExceededException(TimeSpan.FromMinutes(15));

            return default;
        }
    }
}
