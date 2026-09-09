using ActualChat.Chat.Module;
using ActualChat.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

// Same-language text goes to the translator with no detection first (that would double the LLM
// calls), so the prompt's NO_TRANSLATION_NEEDED rule is what keeps it - and the model sometimes
// answers in English instead. The stored translation is checked by script to catch that.
[Collection(nameof(SameLanguageTranslationCollection))]
public class SameLanguageTranslationTest(
    SameLanguageTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<SameLanguageTranslationCollection.AppHostFixture>(fixture, @out)
{
    private const string ShortRussianText = "Привет! Как дела?";
    private const string LongRussianText =
        "Привет! Как дела? Это сообщение достаточно длинное, чтобы перевод пошёл потоком, а не одним ответом.";

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private ITranslations Translations => Tester.Translations;
    private FakeTranslator Translator => field ??= (FakeTranslator)Tester.AppServices.GetRequiredService<Translator>();

    [Fact]
    public async Task AnswerInAnotherScriptShouldKeepTheOriginal()
    {
        // arrange
        Translator.Respond = static (_, _) => "Hi! How are you?";
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, ShortRussianText);

        // act
        var translation = await WhenTranslated(entry.Id, Languages.Russian);

        // assert
        translation.Content.Should().Be(ShortRussianText);
        translation.SourceContentHash.Should().Be(entry.ContentHash);
    }

    [Fact]
    public async Task StreamedAnswerInAnotherScriptShouldKeepTheOriginal()
    {
        // arrange
        Translator.Respond = static (_, _) => "Hi! How are you? This message is long enough to be translated as a stream.";
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, LongRussianText);

        // act
        var translation = await WhenTranslated(entry.Id, Languages.Russian);

        // assert
        translation.Content.Should().Be(LongRussianText);
        translation.SourceContentHash.Should().Be(entry.ContentHash);
    }

    // Fixed-language voice entries used to be stored with no languages, which the client reads as
    // foreign, so they take the same route as typed text
    [Fact]
    public async Task VoiceEntryAnswerInAnotherScriptShouldKeepTheOriginal()
    {
        // arrange
        Translator.Respond = static (_, _) => "Hi! How are you?";
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var streamingEntry = await Tester.CreateStreamingEntry(chatId, Languages.Russian);
        await Tester.UpdateEntryLanguage(streamingEntry.EntryLanguage with { Languages = [] });
        var entry = await FinalizeEntry(streamingEntry.ChatEntrySlim, ShortRussianText);

        // act
        var translation = await WhenTranslated(entry.Id, Languages.Russian);

        // assert
        translation.Content.Should().Be(ShortRussianText);
        translation.SourceContentHash.Should().Be(entry.ContentHash);
    }

    [Fact]
    public async Task TextInAnotherLanguageShouldBeTranslated()
    {
        // arrange
        Translator.Respond = FakeTranslator.Translated;
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        const string text = "Hi! How are you?";
        var entry = await Tester.CreateTextEntry(chatId, text);

        // act
        var translation = await WhenTranslated(entry.Id, Languages.Russian);

        // assert
        translation.Content.Should().Be(FakeTranslator.Translated(text, Languages.Russian));
        translation.SourceContentHash.Should().Be(entry.ContentHash);
    }

    // Private methods

    // Unlike FinalizeStreamingEntry, leaves the entry language as the audio pipeline does:
    // under the hash of the still-empty streaming entry.
    private Task<ChatEntry> FinalizeEntry(ChatEntry streamingEntry, string text)
        => Tester.Commander.Call(new ChatsBackend_ChangeEntry(
            streamingEntry.Id,
            streamingEntry.Version,
            Change.Update(new ChatEntryDiff {
                Content = text,
                ContentStreamId = "",
                Audio = new ChatEntryAudio { MediaId = MediaId.Parse("fake:mediaid") },
                EndsAt = Tester.AppServices.Clocks().SystemClock.Now,
            })));

    private Task<Translation> WhenTranslated(ChatEntryId id, Language language)
        => ComputedTest.When(async ct => {
                var translation = await Translations.Get(Tester.Session, TranslationId.New(id, language), true, ct).Require();
                translation.IsStreaming.Should().BeFalse();
                return translation;
            },
            TimeSpan.FromSeconds(10));
}

[CollectionDefinition(nameof(SameLanguageTranslationCollection))]
public sealed class SameLanguageTranslationCollection : ICollectionFixture<SameLanguageTranslationCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture(
            "same-language-translation",
            messageSink,
            TestAppHostOptions.Default with {
                ConfigureHost = (_, cfg) => {
                    cfg.AddInMemory<ChatSettings>((x => x.IsTranslationEnabled, "true"));
                    cfg.AddInMemory<CoreServerSettings>((x => x.OpenAIKey, "test-key"));
                },
                ConfigureServices = (_, services) => {
                    services.AddSingleton<Translator>(c => new FakeTranslator(c));
                },
            });
}
