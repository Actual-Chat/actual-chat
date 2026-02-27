using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(TranslationCollection))]
[Trait("Category", "Nightly")]
public class TranslationTest(TranslationCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<TranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private IDbEntityResolver<string, DbChatEntryLanguage> LanguageEntityResolver => field ??= Tester.AppServices.DbEntityResolver<string, DbChatEntryLanguage>();
    private ITranslations Translations => Tester.Translations;

    [Theory]
    [InlineData("Hi! How are you?", "en")]
    // [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", "en,ru,tr,es")]
    public async Task ShouldFastReturnNullIfLanguageNotDetectedYet(string text, string sExpectedLanguages)
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);

        // act
        var entry = await Tester.CreateTextEntry(chatId, text);
        var entryLanguage = await Tester.GetEntryLanguage(entry.Id, CancellationToken.None);

        // assert
        entryLanguage.Should().BeNull();

        // assert
        await WhenDetected(entry.Id, sExpectedLanguages);
    }

    [Fact]
    public async Task ShouldResetDetectedLanguageWhenEntryContentUpdated()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, "Hi! How are you?");
        await WhenDetected(entry.Id, "en");

        // act
        await Tester.UpdateTextEntry(entry.Id, "Привет! Как дела?");

        // assert
        await WhenDetected(entry.Id, "ru");
    }

    [Fact]
    public async Task ShouldNotDetectLanguagesWithoutDemand()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Hi! How are you?");

        // assert
        await WhenNotDetected(entry.Id);

        // act
        await Tester.UpdateTextEntry(entry.Id, "Привет! Как дела?");

        // assert
        await WhenNotDetected(entry.Id);

        // act
        await Tester.RemoveTextEntry(entry.Id);

        // assert
        await WhenNotDetected(entry.Id);
    }

    [Fact]
    public async Task ShouldRemoveDetectedLanguageWhenEntryRemoved()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, "Hi! How are you?");
        await WhenDetected(entry.Id, "en");

        // act
        await Tester.RemoveTextEntry(entry.Id);

        // assert
        await WhenNotDetected(entry.Id);
    }

    [Theory]
    [InlineData("Hi! How are you?", "ru", "Привет! Как дела?")]
    [InlineData("Привет! Как дела?", "en", "Hi! How are you?")]
    [InlineData("Merhaba! Nasılsın?", "ru", "Привет! Как дела?")]
    [InlineData("Hola, cómo estás?", "en", "Hi! How are you?")]
    public async Task ShouldTranslateMessage(string sourceText, string targetLanguage, string expectedTranslation)
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, sourceText);
        var targetLang = Language.Parse(targetLanguage);

        await ComputedTest.When(async ct => {
            // act
            var translation = await Translations.Get(Tester.Session, TranslationId.New((ChatEntryId)entry.Id, targetLang), true, ct);

            // assert
            translation.Should().NotBeNull();
            translation.Content.Should().BeSimilarTo(expectedTranslation, 0.7);
        },
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ShouldNotUseRemovedMessagesForContext()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var contextEntry = await Tester.CreateTextEntry(chatId, "I was walking down the river");
        var entry = await Tester.CreateTextEntry(chatId, "I saw a bank");
        await Tester.RemoveTextEntry(contextEntry.Id);
        var targetLang = Languages.Russian;

        // act
        await ComputedTest.When(async ct => {
                // act
                var translation = await Translations.Get(Tester.Session, TranslationId.New((ChatEntryId)entry.Id, targetLang), true, ct);

                // assert
                translation.Should().NotBeNull();
                translation.Content.Should().BeSimilarTo("Я увидел банк", 0.7);
            },
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ShouldNotConfuseLanguage()
    {
        // arrange
        const int count = 10;
        var original = """
                       Forgot about migrations)
                       The only reason I still have EF in my project).
                       In my tests EF was always 10-20% slower (sometimes even more) in queries comparing to Dapper/Npgsql and it creates 30-50% more garbage for GC.
                       In some cases Npgsql allows you deserialize data to ArrayPool directly reducing allocations.
                       I know that I can use compiled queries in EF to match the speed, but it is so inconvinient (and still memory inefficient) that it is simplier to write SQL directly with Dapper/Npgsql.
                       """;
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var targetLanguage = Languages.English;
        var entries = await Enumerable.Range(0, count)
            .Select(async i => {
                var entry = await Tester.CreateTextEntry(chatId, original);
                // Set the language to English - this allows the translation system to skip
                // unnecessary LLM calls when translating English to English
                await Tester.CreateEntryLanguage(entry.Id, Languages.English, entry.ContentHash);
                _ = Tester.GetTranslation(TranslationId.New((ChatEntryId)entry.Id, targetLanguage), true, CancellationToken.None);
                return entry;
            })
            .Collect(1);
        foreach (var entry in entries) {
            // act
            var translation = await WhenTranslated((ChatEntryId)entry.Id, targetLanguage);

            // assert
            if (!translation.Content.IsNullOrEmpty())
                translation.Content.Should()
                    .BeSimilarTo(original, 0.7, "entry #{0} should remain in English", entry.Id);
        }
    }

    [Fact(Skip = "Ignored")] // TODO: enable when translation is better
    public async Task ShouldTranslateWithContextFromPreviousMessages()
    {
        if (TestRunnerInfo.IsBuildAgent())
            return; // local dev only until model results are stable

        // arrange
        const double minSimilarity = 0.7;
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var messages = new (string Text, string ExpectedTranslation, string MainExpectedWord)[]
        {
            ("The bank is closed today", "Банк сегодня закрыт", "Банк"),
            ("The bank has high walls", "У банка высокие стены", "Банк"),
            ("The bank is near the river", "Банк находится рядом с рекой", "Банк"),
            ("The river bank is steep", "Берег реки крутой", "Берег"),
            ("The bank is covered with grass", "Берег покрыт травой", "Берег"),
            ("The bank needs maintenance", "Берег нуждается в обслуживании", "Берег"),
            ("The bank is eroding", "Берег размывается", "Берег"),
            ("The bank is dangerous", "Берег опасен", "Берег"),
            ("The bank should be strengthened", "Берег должен быть укреплен", "Берег"),
        };
        var entries = await messages
            .Select(x => Tester.CreateTextEntry(chatId, x.Text))
            .Collect(1);

        // act
        var translations = await Enumerable.Range(0, messages.Length)
            .Select(i => WhenTranslated((ChatEntryId)entries[i].Id, Languages.Russian))
            .Collect(1);

        // assert
        for (var i = 0; i < messages.Length; i++) {
            var (_, expectedTranslation, mainExpectedWord) = messages[i];
            var translation = translations[i];
            translation.Should().NotBeNull();
            translation.Content.TrimNonWord()
                .Should()
                .NotBeNullOrEmpty()
                .And.BeSimilarTo(expectedTranslation, minSimilarity)
                .And.ContainWord(mainExpectedWord);
        }
        return;
    }

    private Task<Translation> WhenTranslated(ChatEntryId id, Language language)
        => ComputedTest.When(async ct => {
                var translation = await Translations.Get(Tester.Session, TranslationId.New(id, language), true, ct).Require();
                translation.IsStreaming.Should().BeFalse();
                return translation;
            },
            TimeSpan.FromSeconds(10));

    private Task<Language[]> WhenDetected(ChatEntryId id, string sExpectedLanguages, TimeSpan? timeout = null)
    {
        var expectedLanguages = sExpectedLanguages.Split([',']).Select(Language.Parse).ToList();
        return ComputedTest.When(async ct => {
                var language = await Translations.GetLanguage(Tester.Session, (ChatEntryId)id, ct).Require();
                language.Languages.Should().BeEquivalentTo(expectedLanguages, "expected {0} for #{1}", sExpectedLanguages, id);
                return language.Languages;
            },
            (timeout ?? (TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(20)))
            .Debuggable());
    }

    // DB query avoids triggering translation
    private Task WhenNotDetected(ChatEntryId id, TimeSpan? timeout = null)
        => TestsExt.When(() => LanguageEntityResolver.Get(id.Value).RequireNull(),
            Intervals.Fixed(TimeSpan.FromSeconds(0.5)),
            (timeout ?? (TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(20)))
            .Debuggable());
}
