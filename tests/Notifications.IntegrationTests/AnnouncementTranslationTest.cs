using ActualChat.Chat.Module;
using ActualChat.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(AnnouncementTranslationCollection))]
public class AnnouncementTranslationTest(
    AnnouncementTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out
) : SharedAppHostTestBase<AnnouncementTranslationCollection.AppHostFixture>(fixture, @out)
{
    private static readonly ChatId ChatId = Constants.Chat.AnnouncementsChatId;

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FakeTranslator Translator => (FakeTranslator)AppHost.Services.GetRequiredService<Translator>();

    [Fact]
    public async Task AnnouncementShouldReachEachReaderInTheirUILanguage()
    {
        // arrange
        var russianReader = await Tester.SignInAsUniqueAlice();
        await SetUILanguage(Languages.Russian);
        var germanReader = await Tester.SignInAsNew("Carol");
        await SetUILanguage(Languages.German);
        await Tester.SignInAsUniqueBob();
        const string text = "A new release is out";

        // act
        var entry = await Announce(text);

        // assert
        var expectations = new[] {
            (russianReader, FakeTranslator.Translated(text, Languages.Russian)),
            (germanReader, FakeTranslator.Translated(text, Languages.German)),
        };
        foreach (var (reader, expectedText) in expectations) {
            var notification = await Tester.WaitForChatEntryNotification(reader.Id, entry.Id);
            notification.LeadText.Should().Be(expectedText);
        }
    }

    [Fact]
    public async Task AnnouncementShouldReachAReaderInTheLanguageItIsWrittenInAsWritten()
    {
        // arrange
        var reader = await Tester.SignInAsUniqueAlice();
        await SetUILanguage(Languages.English);
        await Tester.SignInAsUniqueBob();
        const string text = "Nothing to translate here";

        // act
        var entry = await Announce(text);

        // assert
        var notification = await Tester.WaitForChatEntryNotification(reader.Id, entry.Id);
        notification.LeadText.Should().Be(text);
    }

    [Fact]
    public async Task AnnouncementShouldReachAReaderWhoTurnedTranslationOffAsWritten()
    {
        // arrange
        var reader = await Tester.SignInAsUniqueAlice();
        await SetUILanguage(Languages.Russian);
        await Tester.AppServices.UserSettingsUI(Tester.Session)
            .ChatUserSettings(ChatId)
            .Update(x => x with { MustTranslate = false }, CancellationToken.None);
        await Tester.SignInAsUniqueBob();
        const string text = "Maintenance tonight";

        // act
        var entry = await Announce(text);

        // assert
        var notification = await Tester.WaitForChatEntryNotification(reader.Id, entry.Id);
        notification.LeadText.Should().Be(text);
    }

    [Fact]
    public async Task AnnouncementShouldGoOutAsWrittenWhenTranslationFails()
    {
        // arrange
        var reader = await Tester.SignInAsUniqueAlice();
        await SetUILanguage(Languages.Russian);
        await Tester.SignInAsUniqueBob();
        const string text = "The translator is down";
        Translator.MustFail = true;

        try {
            // act
            var entry = await Announce(text);

            // assert
            var notification = await Tester.WaitForChatEntryNotification(reader.Id, entry.Id);
            notification.LeadText.Should().Be(text, "a push must not be lost to a failed translation");
        }
        finally {
            Translator.MustFail = false;
        }
    }

    // Private methods

    private async Task<ChatEntry> Announce(string text)
    {
        // Through the backend: only the chat's owners may post, and a test host has none.
        // The wait: a new account joins the chat shortly after it signs in.
        var author = await TestWait.When(ct => Tester.GetOwnAuthor(ChatId, ct).Require());
        var command = new ChatsBackend_ChangeEntry(
            ChatEntryId.New(ChatId, 0),
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author.Id,
                Content = text,
            }));
        return await Commander.Call(command);
    }

    private Task SetUILanguage(Language language)
        => Tester.AppServices.UserSettingsUI(Tester.Session)
            .UserLanguageSettings()
            .Update(x => x with { UILanguage = language }, CancellationToken.None);
}

[CollectionDefinition(nameof(AnnouncementTranslationCollection))]
public sealed class AnnouncementTranslationCollection
    : ICollectionFixture<AnnouncementTranslationCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture(
            "announcement-translation",
            messageSink,
            TestAppHostOptions.WithAnnouncementsChat with {
                ConfigureHost = (_, cfg) => {
                    cfg.AddInMemory<ChatSettings>((x => x.IsTranslationEnabled, "true"));
                    cfg.AddInMemory<CoreServerSettings>((x => x.OpenAIKey, "test-key"));
                },
                ConfigureServices = (_, services) => {
                    services.AddSingleton<Translator>(c => new FakeTranslator(c));
                    services.AddKeyedSingleton<Translator>(
                        Constants.Translation.RealtimeServiceKey,
                        (c, key) => new FakeTranslator(c, (string)key));
                },
            });
}
