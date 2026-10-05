using System.Net;
using ActualChat.Chat.ML;
using ActualChat.Contacts;
using ActualChat.Localization;
using ActualChat.Testing.Host;
using ActualChat.Users.Email;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Users.IntegrationTests;

public class EmailLocalizationTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(EmailLocalizationTest)}", TestAppHostOptions.Default, @out)
{
    // The non-production ICaptcha implementation resolves this token without calling reCAPTCHA
    private const string PassingProofToken = "fake-pass";

    [Fact]
    public async Task SignInCodeShouldUseLanguageDetectedForGuest()
    {
        // arrange
        var sender = new CapturingEmailSender();
        await using var h = await NewAppHost(sender);
        await using var tester = h.NewWebClientTester(Out);
        var guest = await tester.Accounts.GetOwn(tester.Session, default);
        await SetLanguageSettings(h, guest.Id, x => x with { DetectedUILanguage = Languages.Spanish });
        var l = LanguageStringLocalizer.Get(Languages.Spanish);

        // act
        var mail = await SendCode(tester, sender, TotpPurpose.SignInEmail);

        // assert
        guest.IsGuest.Should().BeTrue();
        mail.Subject.Should().Be(l.EmailCode_SignInSubject_Format(CoreConstants.AppName));
        mail.Text.Should().Contain("lang=\"es\"");
        mail.Text.Should().Contain(l.EmailCode_Title);
        mail.Text.Should().Contain(l.EmailCode_Warning);
        mail.Text.Should().Contain(l.Documents_PrivacyPolicy);
    }

    [Fact]
    public async Task VerificationCodeShouldPreferSelectedLanguage()
    {
        // arrange
        var sender = new CapturingEmailSender();
        await using var h = await NewAppHost(sender);
        await using var tester = h.NewWebClientTester(Out);
        var account = await tester.SignInAsNew("EmailCode");
        await SetLanguageSettings(h, account.Id, x => x with {
            UILanguage = Languages.Russian,
            DetectedUILanguage = Languages.Spanish,
        });
        var l = LanguageStringLocalizer.Get(Languages.Russian);

        // act
        var mail = await SendCode(tester, sender, TotpPurpose.VerifyEmail);

        // assert
        mail.Subject.Should().Be(l.EmailCode_VerifySubject_Format(CoreConstants.AppName));
        mail.Text.Should().Contain("lang=\"ru\"");
        mail.Text.Should().Contain(l.EmailCode_Title);
    }

    [Fact]
    public async Task CodeShouldFallBackToEnglishWithoutLanguageSettings()
    {
        // arrange
        var sender = new CapturingEmailSender();
        await using var h = await NewAppHost(sender);
        await using var tester = h.NewWebClientTester(Out);
        var l = LanguageStringLocalizer.Get(Languages.English);

        // act
        var mail = await SendCode(tester, sender, TotpPurpose.SignInEmail);

        // assert
        mail.Subject.Should().Be(l.EmailCode_SignInSubject_Format(CoreConstants.AppName));
        mail.Text.Should().Contain(l.EmailCode_Title);
    }

    [Fact]
    public async Task DigestShouldUseUILanguageAndSummarizeInSpokenLanguage()
    {
        // arrange
        var sender = new CapturingEmailSender();
        var summarizer = new FakeDigestSummarizer();
        await using var h = await NewAppHost(sender, summarizer);
        await using var tester = h.NewWebClientTester(Out);
        var account = await tester.SignInAsNew("Digest");
        await SetLanguageSettings(h, account.Id, x => x with {
            Primary = Languages.English,
            UILanguage = Languages.Spanish,
        });
        await CreateChatWithUnreadEntries(h, tester, account.Id);
        var l = LanguageStringLocalizer.Get(Languages.Spanish);

        // act
        await h.Services.Commander().Call(new EmailsBackend_SendDigest(account.Id));

        // assert
        var mail = sender.Sent.Should().ContainSingle().Subject;
        mail.Subject.Should().Be(l.EmailDigest_Subject_Format(CoreConstants.AppName));
        mail.Text.Should().Contain("lang=\"es\"");
        mail.Text.Should().Contain(l.EmailDigest_OpenUnread(2, 2, "Digest chat"));
        mail.Text.Should().Contain(l.EmailDigest_Reason_Format(CoreConstants.AppName));
        mail.Text.Should().Contain(l.EmailDigest_TurnOff);
        mail.Text.Should().Contain(FakeDigestSummarizer.Summary);
        summarizer.Languages.Should().Equal([Languages.English],
            "the summary follows the spoken language, only the chrome follows the UI language");
    }

    [Fact]
    public async Task DigestShouldNotSummarizeAnnouncements()
    {
        // arrange
        var sender = new CapturingEmailSender();
        var summarizer = new FakeDigestSummarizer();
        await using var h = await NewAppHost(options => options with {
            ChatDbInitializerOptions = TestAppHostOptions.WithAnnouncementsChat.ChatDbInitializerOptions,
            ConfigureServices = (_, services) => {
                services.Replace(ServiceDescriptor.Singleton<IEmailSender>(sender));
                services.Replace(ServiceDescriptor.Singleton<IChatDigestSummarizer>(summarizer));
            },
        });
        await using var tester = h.NewWebClientTester(Out);
        var account = await tester.SignInAsNew("Digest");
        var chatId = Constants.Chat.AnnouncementsChatId;
        var authors = h.Services.GetRequiredService<IAuthorsBackend>();
        var author = await authors
            .GetByUserId(chatId, Constants.User.Admin.UserId, RequestedAuthorKind.Full, default)
            .Require();
        await h.Services.Commander().Call(new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chatId, 0),
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author.Id,
                Content = "A new Voxt announcement",
                BeginsAt = h.Services.Clocks().SystemClock.Now,
            })));

        // act
        await h.Services.Commander().Call(new EmailsBackend_SendDigest(account.Id));

        // assert
        sender.Sent.Should().BeEmpty();
        summarizer.Languages.Should().BeEmpty();
    }

    [Fact]
    public async Task DigestShouldUseChosenDigestLanguageForChromeAndSummary()
    {
        // arrange
        var sender = new CapturingEmailSender();
        var summarizer = new FakeDigestSummarizer();
        await using var h = await NewAppHost(sender, summarizer);
        await using var tester = h.NewWebClientTester(Out);
        var account = await tester.SignInAsNew("DigestLanguage");
        await SetLanguageSettings(h, account.Id, x => x with {
            Primary = Languages.English,
            UILanguage = Languages.Spanish,
        });
        await h.Services.GetRequiredService<IServerKvasBackend>()
            .ForUser(account.Id)
            .UserEmailsSettings()
            .Update(x => x with { DigestLanguage = Languages.German }, default);
        await CreateChatWithUnreadEntries(h, tester, account.Id);
        var l = LanguageStringLocalizer.Get(Languages.German);
        var uiL = LanguageStringLocalizer.Get(Languages.Spanish);

        // act
        await h.Services.Commander().Call(new EmailsBackend_SendDigest(account.Id));

        // assert
        var mail = sender.Sent.Should().ContainSingle().Subject;
        mail.Subject.Should().Be(l.EmailDigest_Subject_Format(CoreConstants.AppName));
        mail.Text.Should().Contain("lang=\"de\"");
        mail.Text.Should().Contain(l.EmailDigest_OpenUnread(2, 2, "Digest chat"));
        mail.Text.Should().Contain(l.EmailDigest_TurnOff);
        mail.Text.Should().NotContain(uiL.EmailDigest_TurnOff, "the chosen digest language replaces the UI language");
        summarizer.Languages.Should().Equal([Languages.German],
            "the chosen digest language replaces the spoken one in the summary as well");
    }

    // Private methods

    private Task<TestAppHost> NewAppHost(CapturingEmailSender sender, FakeDigestSummarizer? summarizer = null)
        => NewAppHost(options => options with {
            ConfigureServices = (_, services) => {
                services.Replace(ServiceDescriptor.Singleton<IEmailSender>(sender));
                if (summarizer is not null)
                    services.Replace(ServiceDescriptor.Singleton<IChatDigestSummarizer>(summarizer));
            },
        });

    private static async Task CreateChatWithUnreadEntries(TestAppHost h, IWebClientTester tester, UserId userId)
    {
        var (chatId, _) = await tester.CreateChat(x => x with { Title = "Digest chat" });
        await h.Services.WaitForOpeningEntry(chatId);
        var entries = await tester.CreateTextEntries(chatId, "message", 3);
        var readPosition = new ChatPosition(entries[0].LocalId);
        await h.Services.Commander().Call(
            new ChatPositionsBackend_Set(userId, chatId, ChatPositionKind.Read, readPosition, true));
        var contactsBackend = h.Services.GetRequiredService<IContactsBackend>();
        await TestWait.When(async ct => {
            var contactIds = await contactsBackend.ListIdsForSearch(userId, ContactSubset.All(), true, ct);
            contactIds.Should().Contain(x => x.ChatId == chatId, "the digest lists chats from the contact list");
        });
    }

    private static Task SetLanguageSettings(
        TestAppHost h, UserId userId, Func<UserLanguageSettings, UserLanguageSettings> updater)
        => h.Services.GetRequiredService<IServerKvasBackend>()
            .ForUser(userId)
            .UserLanguageSettings()
            .Update(updater, default);

    private static async Task<SentEmail> SendCode(
        IWebClientTester tester, CapturingEmailSender sender, TotpPurpose purpose)
    {
        var email = ActualChat.Email.Parse($"{Ulid.NewUlid().ToString().ToLower()}@example.com");
        await tester.Commander.Call(new EmailAuth_SendTotp {
            Session = tester.Session,
            Email = email,
            Purpose = purpose,
            CaptchaToken = PassingProofToken,
            CaptchaAction = Constants.Recaptcha.Actions.ForPurpose(purpose),
        });
        return sender.Sent.Should().ContainSingle(x => x.Email == email.Value).Subject;
    }

    // Nested types

    // The renderer escapes everything outside Basic Latin, so the body is kept decoded
    private sealed record SentEmail(string Email, string Subject, string Text);

    private sealed class CapturingEmailSender : IEmailSender
    {
        public ConcurrentQueue<SentEmail> Sent { get; } = new();

        public Task Send(
            string name, string email, string subject, string html,
            string? unsubscribeUrl, CancellationToken cancellationToken)
        {
            Sent.Enqueue(new SentEmail(email, subject, WebUtility.HtmlDecode(html)));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDigestSummarizer : IChatDigestSummarizer
    {
        public const string Summary = "summary-of-the-day";

        public ConcurrentQueue<Language> Languages { get; } = new();

        public Task<IReadOnlyCollection<string>> Summarize(
            IReadOnlyCollection<ChatEntry> chatEntries, Language language, CancellationToken cancellationToken)
        {
            Languages.Enqueue(language);
            return Task.FromResult<IReadOnlyCollection<string>>([Summary]);
        }

        public Task<string?> SummarizeMediaShares(
            IReadOnlyCollection<ChatEntry> mediaEntries, Language language, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }
}
