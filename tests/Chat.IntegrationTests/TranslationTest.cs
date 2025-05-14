using System.Diagnostics.CodeAnalysis;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(TranslationCollection))]
public class TranslationTest(TranslationCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<TranslationCollection.AppHostFixture>(fixture, @out)
{
    [field: AllowNull, MaybeNull]
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private ITranslations Translations => Tester.Translations;

    [Theory]
    [InlineData("Hi! How are you?", 1, "en")]
    [InlineData("Hi! How are you?", 10, "en")]
    // [InlineData("Hi! How are you?", 111, "en")]
    [InlineData("Привет! Как дела?", 1, "ru")]
    [InlineData("Привет! Как дела?", 11, "ru")]
    //[InlineData("Привет! Как дела?", 50, "ru")]
    [InlineData("Merhaba! Nasılsın?", 1, "tr")]
    [InlineData("Merhaba! Nasılsın?", 12, "tr")]
    //[InlineData("Hola, cómo estás?", 50, "es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", 1, "en,ru,tr,es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", 13, "en,ru,tr,es")]
    //[InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", 77, "en,ru,tr,es")]
    public async Task ShouldDetectLanguageOnInsert(string text, int entryCount, string sExpectedLanguages)
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);

        // act
        var entries = await CreateEntries(chatId, text, entryCount);

        // assert
        await entries.Select(x => WhenDetected(x.Id, sExpectedLanguages)).Collect();
    }

    [Theory]
    [InlineData("Hi! How are you?", 1, "en")]
    [InlineData("Hi! How are you?", 10, "en")]
    // [InlineData("Hi! How are you?", 50, "en")]
    [InlineData("Привет! Как дела?", 1, "ru")]
    [InlineData("Привет! Как дела?", 10, "ru")]
    // [InlineData("Привет! Как дела?", 50, "ru")]
    [InlineData("Merhaba! Nasılsın?", 1, "tr")]
    [InlineData("Merhaba! Nasılsın?", 10, "tr")]
    // [InlineData("Merhaba! Nasılsın?", 50, "tr")]
    [InlineData("Hola, cómo estás?", 1, "es")]
    [InlineData("Hola, cómo estás?", 10, "es")]
    // [InlineData("Hola, cómo estás?", 50, "es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", 1, "en,ru,tr,es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", 10, "en,ru,tr,es")]
    // [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", 77, "en,ru,tr,es")]
    public async Task ShouldDetectLanguageOnUpdate(string updatedText, int entryCount, string sExpectedLanguages)
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);

        // act
        var entries = await CreateEntries(chatId, "some text", entryCount);
        var updatedEntries = await UpdateEntries(updatedText, entries);

        // assert
        await updatedEntries.Select(x => WhenDetected(x.Id, sExpectedLanguages)).Collect();
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
            var translation = await Translations.Get(Tester.Session, new TranslationId(entry.Id, targetLang, AssumeValid.Option), ct);

            // assert
            translation.Should().NotBeNull();
            translation.Content.Should().BeSimilarTo(expectedTranslation, 0.7);
        },
            TimeSpan.FromSeconds(10));
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
            .Select(i => WhenTranslated(entries[i].Id, Languages.Russian))
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

        Task<Translation> WhenTranslated(ChatEntryId id, Language language)
            => ComputedTest.When(
                ct => Translations.Get(Tester.Session, new(id, language, AssumeValid.Option), ct).Require(),
                TimeSpan.FromSeconds(10));
    }

    private async Task<ChatEntry[]> CreateEntries(ChatId chatId, string text, int entryCount)
    {
        var entries = await Enumerable.Repeat(0, entryCount)
            .Select(async _ => await Tester.CreateTextEntry(chatId, text).ConfigureAwait(false))
            .Collect(2);
        return entries;
    }

    private Task<ChatEntry[]> UpdateEntries(string updatedText, ChatEntry[] entries)
        => entries.Select(x => Tester.UpdateTextEntry(x.Id, updatedText)).Collect(2);

    private Task<Language[]> WhenDetected(ChatEntryId id, string sExpectedLanguages, TimeSpan? timeout = null)
    {
        var expectedLanguages = sExpectedLanguages.Split([',']).Select(Language.Parse).ToList();
        return ComputedTest.When(async ct => {
                var language = await Translations.GetLanguage(Tester.Session, id, ct).Require();
                language.Languages.Should().BeEquivalentTo(expectedLanguages, "expected {0} for #{1}", sExpectedLanguages, id);
                return language.Languages;
            },
            (timeout ?? (TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(20)))
            .Debuggable());
    }
}
