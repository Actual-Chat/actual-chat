using ActualChat.App.Server.Module;
using ActualChat.Resilience;
using ActualChat.Testing.Host;
using ActualChat.Users.Module;
using ActualChat.Users.Phone;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Users.IntegrationTests;

// Deliberately keeps calling the obsolete PhoneAuth_SendTotp throughout this suite so its
// end-to-end delegation to PhoneAuth_SendCode stays covered for old clients.
#pragma warning disable CS0618
[Collection(nameof(UserCollection))]
public class TotpCodesTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    // Tokens the non-production ICaptcha implementation resolves without calling reCAPTCHA
    private const string PassingProofToken = "fake-pass";
    private const string FailingProofToken = "fake-fail";
    private TotpCodes TotpCodes => AppHost.Services.GetRequiredService<TotpCodes>();
    private UsersSettings Settings => AppHost.Services.GetRequiredService<UsersSettings>();
    private CaptchaProofValidator CaptchaProofs => AppHost.Services.GetRequiredService<CaptchaProofValidator>();

    [Fact(Timeout = 60_000)]
    public async Task AuthenticationBackendsShouldShareStateAcrossApiAndBackendHosts()
    {
        // arrange
        var meshLockSubspace = Alphabet.AlphaNumeric.Generator8.Next();
        await using var backendHost = await NewAppHost(AppHost.Options.InstanceName,
            options => WithRole(options, HostRole.OneBackendServer));
        await using var apiHost = await NewAppHost(AppHost.Options.InstanceName,
            options => WithRole(options, HostRole.OneApiServer));
        var apiWatcher = apiHost.Services.MeshWatcher();
        var backendWatcher = backendHost.Services.MeshWatcher();
        await apiWatcher.WhenAnnounced;
        await backendWatcher.WhenAnnounced;
        await TestWait.When(async ct => {
            (await apiWatcher.State.Use(ct)).AllNodes.Count.Should().BeGreaterThanOrEqualTo(2);
            (await backendWatcher.State.Use(ct)).AllNodes.Count.Should().BeGreaterThanOrEqualTo(2);
        });
        var apiCodes = apiHost.Services.GetRequiredService<ITotpCodesBackend>();
        var backendCodes = backendHost.Services.GetRequiredService<ITotpCodesBackend>();
        apiHost.Services.HostInfo().Roles.GetBackendServiceMode<ITotpCodesBackend>().Should().Be(ServiceMode.Client);
        var (purpose, target) = NewInputs();
        var phone = NewPhone();
        var apiPasskeys = apiHost.Services.GetRequiredService<IPasskeysBackend>();
        var backendPasskeys = backendHost.Services.GetRequiredService<IPasskeysBackend>();
        var userId = UserId.NewGuest();
        var prefix = $".PasskeyTest:{Ulid.NewUlid()}:";

        // act
        var code = await apiCodes.Generate(target, purpose);
        var validations = await Task.WhenAll(
            apiCodes.Validate(target, purpose, code),
            backendCodes.Validate(target, purpose, code));
        var isFirstThrottled = await apiCodes.IsPhoneThrottled(phone, default);
        var isSecondThrottled = await backendCodes.IsPhoneThrottled(phone, default);
        await apiCodes.SetLastChannel(phone, TotpChannel.Sms, default);
        var channel = await backendCodes.GetLastChannel(phone, default);
        await apiPasskeys.StoreChallenge(userId, prefix, "pending-challenge", default);
        var consumeAsOther = () => backendPasskeys.ConsumeChallenge(UserId.NewGuest(), prefix, default);

        // assert
        await consumeAsOther.Should().ThrowAsync<InvalidOperationException>().WithMessage("*expired*");
        (await backendPasskeys.ConsumeChallenge(userId, prefix, default)).Should().Be("pending-challenge");
        var consumeAgain = () => apiPasskeys.ConsumeChallenge(userId, prefix, default);
        await consumeAgain.Should().ThrowAsync<InvalidOperationException>().WithMessage("*expired*");
        validations.Should().ContainSingle(x => x);
        isFirstThrottled.Should().BeFalse();
        isSecondThrottled.Should().BeTrue();
        channel.Should().Be(TotpChannel.Sms);
        return;

        TestAppHostOptions WithRole(TestAppHostOptions options, HostRole role) {
            return options with {
                MustInitializeDb = false,
                MeshLockSubspace = meshLockSubspace,
                ConfigureHost = (ctx, cfg) => {
                    options.ConfigureHost?.Invoke(ctx, cfg);
                    cfg.AddInMemory<HostSettings>((x => x.ServerRole, role.Value));
                },
            };
        }
    }

    [Fact]
    public async Task ValidateShouldRefuseCodeAfterTooManyAttempts()
    {
        // arrange
        var (purpose, target) = NewInputs();
        var code = await TotpCodes.Generate(target, purpose);
        var maxAttemptCount = Settings.TotpMaxAttemptCount;

        // act
        var wrongResults = new List<bool>();
        for (var i = 0; i < maxAttemptCount; i++)
            wrongResults.Add(await TotpCodes.Validate(target, purpose, NextCode(code + i)));
        var isCorrectAccepted = await TotpCodes.Validate(target, purpose, code);

        // assert
        wrongResults.Should().AllSatisfy(x => x.Should().BeFalse());
        isCorrectAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateShouldAcceptCorrectCodeWhileAttemptsRemain()
    {
        // arrange
        var (purpose, target) = NewInputs();
        var code = await TotpCodes.Generate(target, purpose);

        // act
        var isWrongAccepted = await TotpCodes.Validate(target, purpose, NextCode(code));
        var isCorrectAccepted = await TotpCodes.Validate(target, purpose, code);

        // assert
        isWrongAccepted.Should().BeFalse();
        isCorrectAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task CodeShouldBeScopedToTargetAndPurpose()
    {
        // arrange
        var (purpose, target) = NewInputs();
        var (_, otherTarget) = NewInputs();
        var code = await TotpCodes.Generate(target, purpose);

        // act
        var isOtherTargetAccepted = await TotpCodes.Validate(otherTarget, purpose, code);
        var isOtherPurposeAccepted = await TotpCodes.Validate(target, TotpPurpose.VerifyEmail, code);
        var isCorrectAccepted = await TotpCodes.Validate(target, purpose, code);

        // assert
        isOtherTargetAccepted.Should().BeFalse();
        isOtherPurposeAccepted.Should().BeFalse();
        isCorrectAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task CodeShouldNotBeReusable()
    {
        // arrange
        var (purpose, target) = NewInputs();
        var code = await TotpCodes.Generate(target, purpose);

        // act
        var isFirstAccepted = await TotpCodes.Validate(target, purpose, code);
        var isSecondAccepted = await TotpCodes.Validate(target, purpose, code);

        // assert
        isFirstAccepted.Should().BeTrue();
        isSecondAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task ResendShouldReplaceTheCode()
    {
        // arrange
        var (purpose, target) = NewInputs();

        // act
        var codes = new List<int>();
        for (var i = 0; i < 5; i++)
            codes.Add(await TotpCodes.Generate(target, purpose));
        var isOldAccepted = await TotpCodes.Validate(target, purpose, codes[0]);
        var isNewestAccepted = await TotpCodes.Validate(target, purpose, codes[^1]);

        // assert
        codes.Distinct().Should().HaveCountGreaterThan(1);
        isOldAccepted.Should().BeFalse();
        isNewestAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task CodeShouldStayValidUntilItsLifetimeEnds()
    {
        // arrange
        var (purpose, target) = NewInputs();
        var lifetime = TimeSpan.FromSeconds(3);
        var earlyCode = await TotpCodes.Generate(target, purpose, lifetime, 5, default);

        // act
        await Task.Delay(TimeSpan.FromSeconds(1));
        var isAcceptedWithinLifetime = await TotpCodes.Validate(target, purpose, earlyCode);
        var lateCode = await TotpCodes.Generate(target, purpose, lifetime, 5, default);
        await Task.Delay(lifetime + TimeSpan.FromSeconds(1));
        var isAcceptedAfterLifetime = await TotpCodes.Validate(target, purpose, lateCode);

        // assert
        isAcceptedWithinLifetime.Should().BeTrue();
        isAcceptedAfterLifetime.Should().BeFalse();
    }

    [Fact]
    public async Task PredefinedCodeShouldSignInWithoutASentCode()
    {
        // arrange
        var phone = ActualChat.Phone.New("1", "5555555550");
        var settings = Settings;
        var oldPredefinedTotps = settings.PredefinedTotps;
        settings.PredefinedTotps = new Dictionary<string, int> {
            { ActualChat.Phone.NormalizePart(phone.Value), 111111 },
        };
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);

        // act
        bool isWrongAccepted;
        bool isPredefinedAccepted;
        try {
            await tester.Commander.Call(new PhoneAuth_SendTotp {
                Session = tester.Session,
                Phone = phone,
                Purpose = TotpPurpose.SignInPhone,
            });
            isWrongAccepted = await tester.Commander
                .Call(new PhoneAuth_ValidateTotp { Session = tester.Session, Phone = phone, Totp = 222222 });
            isPredefinedAccepted = await tester.Commander
                .Call(new PhoneAuth_ValidateTotp { Session = tester.Session, Phone = phone, Totp = 111111 });
        }
        finally {
            settings.PredefinedTotps = oldPredefinedTotps;
        }

        // assert
        isWrongAccepted.Should().BeFalse();
        isPredefinedAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task LookupsShouldBeRefusedPastBudget()
    {
        // arrange
        var policy = new LookupRateLimitPolicy(3);
        var hostSuffix = $"lookup-budget-{Ulid.NewUlid().ToString().ToLower()}";
        await using var appHost = await NewAppHost(hostSuffix, options => options with {
            ConfigureServices = (_, services)
                => services.Replace(ServiceDescriptor.Singleton<RateLimitPolicy>(policy)),
        });
        var emailAuth = appHost.Services.GetRequiredService<IEmailAuth>();
        var phoneAuth = appHost.Services.GetRequiredService<IPhoneAuth>();
        var email = ActualChat.Email.Parse($"{Ulid.NewUlid().ToString().ToLower()}@example.com");
        var phone = NewPhone();

        // act
        var emailResults = new List<bool>();
        var phoneResults = new List<bool>();
        for (var i = 0; i < 3; i++) {
            emailResults.Add(await emailAuth.AccountExists(Session.New(), email, default));
            phoneResults.Add(await phoneAuth.AccountExists(Session.New(), phone, default));
        }
        Func<Task> emailPastBudget = () => emailAuth.AccountExists(Session.New(), email, default);
        Func<Task> phonePastBudget = () => phoneAuth.AccountExists(Session.New(), phone, default);

        // assert
        emailResults.Should().AllSatisfy(x => x.Should().BeFalse());
        phoneResults.Should().AllSatisfy(x => x.Should().BeFalse());
        await emailPastBudget.Should().ThrowAsync<RateLimitExceededException>();
        await phonePastBudget.Should().ThrowAsync<RateLimitExceededException>();
    }

    [Fact]
    public async Task SendShouldRefuseInvalidProof()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        var command = new PhoneAuth_SendTotp {
            Session = tester.Session,
            Phone = NewPhone(),
            Purpose = TotpPurpose.SignInPhone,
            CaptchaToken = FailingProofToken,
            CaptchaAction = Constants.Recaptcha.Actions.PhoneSignIn,
        };

        // act
        Func<Task> send = () => tester.Commander.Call(command);

        // assert
        await send.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendShouldAcceptValidProof()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        var command = new PhoneAuth_SendTotp {
            Session = tester.Session,
            Phone = NewPhone(),
            Purpose = TotpPurpose.SignInPhone,
            CaptchaToken = PassingProofToken,
            CaptchaAction = Constants.Recaptcha.Actions.PhoneSignIn,
        };

        // act
        Func<Task> send = () => tester.Commander.Call(command);

        // assert
        await send.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendShouldRefuseProofMintedForAnotherAction()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        var command = new PhoneAuth_SendTotp {
            Session = tester.Session,
            Phone = NewPhone(),
            Purpose = TotpPurpose.SignInPhone,
            CaptchaToken = PassingProofToken,
            CaptchaAction = Constants.Recaptcha.Actions.EmailSignIn,
        };

        // act
        Func<Task> send = () => tester.Commander.Call(command);

        // assert
        await send.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendShouldAcceptRequestWithoutProofWhereCaptchaIsDisabled()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        var command = new PhoneAuth_SendTotp {
            Session = tester.Session,
            Phone = NewPhone(),
            Purpose = TotpPurpose.SignInPhone,
        };

        // act
        var isProofRequired = CaptchaProofs.IsProofRequired;
        Func<Task> send = () => tester.Commander.Call(command);

        // assert
        isProofRequired.Should().BeFalse();
        await send.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendShouldAcceptRequestWithoutProofFromNativeApp()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        await tester.Commander.Call(new SessionsBackend_Upsert(tester.Session) {
            Description = AppKind.Ios.ToUserAgent("1.0"),
        });
        var command = new PhoneAuth_SendTotp {
            Session = tester.Session,
            Phone = NewPhone(),
            Purpose = TotpPurpose.SignInPhone,
        };

        // act
        Func<Task> send = () => tester.Commander.Call(command);

        // assert
        await send.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ThrottledSendShouldReportTheLiveCodesChannel()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        // A native-app session needs no captcha proof, so both sends can go through unchanged
        await tester.Commander.Call(new SessionsBackend_Upsert(tester.Session) {
            Description = AppKind.Ios.ToUserAgent("1.0"),
        });
        var phone = NewPhone();
        var command = new PhoneAuth_SendCode {
            Session = tester.Session,
            Phone = phone,
            Purpose = TotpPurpose.SignInPhone,
        };

        // act - a fresh Uuid on the second send, or the deduplicator would replay the first result
        var sent = await tester.Commander.Call(command);
        var throttled = await tester.Commander.Call(command with { Uuid = ApiCommand.NewUuid() });

        // assert
        sent.Channel.Should().Be(TotpChannel.Sms);
        throttled.NextSendAt.Should().BeGreaterThan(default);
        throttled.Channel.Should().Be(TotpChannel.Sms);
    }

    [Fact]
    public async Task EmailSendShouldRefuseInvalidProof()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        var email = ActualChat.Email.Parse($"{Ulid.NewUlid().ToString().ToLower()}@example.com");
        var command = new EmailAuth_SendTotp {
            Session = tester.Session,
            Email = email,
            Purpose = TotpPurpose.SignInEmail,
            CaptchaToken = FailingProofToken,
            CaptchaAction = Constants.Recaptcha.Actions.EmailSignIn,
        };

        // act
        Func<Task> send = () => tester.Commander.Call(command);

        // assert
        await send.Should().ThrowAsync<InvalidOperationException>();
    }

    // Private methods

    private static (TotpPurpose Purpose, string Target) NewInputs()
        => (TotpPurpose.SignInEmail, $"{Ulid.NewUlid().ToString().ToLower()}@actual.chat");

    private static ActualChat.Phone NewPhone()
        => ActualChat.Phone.New("1", $"555{Random.Shared.Next(1_000_000, 9_999_999)}");

    private static int NextCode(int code)
        => (code + 1) % 1_000_000;

    private sealed class LookupRateLimitPolicy(int limit) : RateLimitPolicy
    {
        private readonly Dictionary<string, int> _counts = new();

        public override ValueTask Check(
            string method,
            RateLimitClass rateLimitClass,
            ReadOnlySpan<RateLimitIdentity> identities,
            CancellationToken cancellationToken = default)
        {
            if (rateLimitClass is not RateLimitClass.Auth)
                return default;

            var target = default(RateLimitIdentity);
            foreach (var identity in identities)
                if (identity.Kind is RateLimitIdentityKind.Target) {
                    target = identity;
                    break;
                }
            if (target == default)
                throw new InvalidOperationException("The target identity is missing.");

            var count = _counts.GetValueOrDefault(target.Value) + 1;
            _counts[target.Value] = count;
            if (count <= limit)
                return default;

            throw StandardError.RateLimitExceeded(TimeSpan.FromMinutes(1));
        }
    }
}
#pragma warning restore CS0618
