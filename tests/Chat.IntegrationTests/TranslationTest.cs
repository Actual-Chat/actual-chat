using System.Diagnostics.CodeAnalysis;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(TranslationCollection))]
public class TranslationTest(TranslationCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [field: AllowNull, MaybeNull]
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private ITranslations Translations => Tester.Translations;

    [Theory]
    [InlineData("Hi! How are you?", 1, "en")]
    [InlineData("Hi! How are you?", 50, "en")]
    [InlineData("Привет! Как дела?", 1, "ru")]
    [InlineData("Привет! Как дела?", 50, "ru")]
    [InlineData("Merhaba! Nasılsın?", 1, "tr")]
    [InlineData("Hola, cómo estás?", 50, "es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", 1, "en,ru,tr,es")]
    public async Task ShouldDetectLanguageOnInsert(string text, int entryCount, string sExpectedLanguages)
    {
        if (TestRunnerInfo.IsBuildAgent())
            return; // only for local runs for now

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false, "translation lab");

        // act
        var entries = await CreateEntries(chatId, text, entryCount);

        // assert
        await entries.Select(x => WhenDetected(x.Id, sExpectedLanguages)).Collect();
    }

    [Theory]
    [InlineData("Hi! How are you?", 1, "en")]
    [InlineData("Hi! How are you?", 50, "en")]
    [InlineData("Привет! Как дела?", 1, "ru")]
    [InlineData("Привет! Как дела?", 50, "ru")]
    [InlineData("Merhaba! Nasılsın?", 1, "tr")]
    [InlineData("Merhaba! Nasılsın?", 50, "tr")]
    [InlineData("Hola, cómo estás?", 1, "es")]
    [InlineData("Hola, cómo estás?", 50, "es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", 1, "en,ru,tr,es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", 10, "en,ru,tr,es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", 77, "en,ru,tr,es")]
    public async Task ShouldDetectLanguageOnUpdate(string updatedText, int entryCount, string sExpectedLanguages)
    {
        if (TestRunnerInfo.IsBuildAgent())
            return; // only for local runs for now

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false, "translation lab");

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
        if (TestRunnerInfo.IsBuildAgent())
            return; // only for local runs for now

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false, "translation lab");
        var entry = await Tester.CreateTextEntry(chatId, sourceText);
        var targetLang = Language.Parse(targetLanguage);

        await ComputedTest.When(async ct => {
                // act
                var translation = await Translations.Get(Tester.Session, new TranslationId(entry.Id, targetLang, AssumeValid.Option), ct);

                // assert
                translation.Should().NotBeNull();
                TextAssert.ShouldBeSimilar(translation.Content, expectedTranslation, 0.7);
            },
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

    private Task<ApiArray<Language>> WhenDetected(ChatEntryId id, string sExpectedLanguages, TimeSpan? timeout = null)
    {
        var expectedLanguages = sExpectedLanguages.Split([',']).Select(Language.Parse).ToList();
        return ComputedTest.When(async ct => {
                var language = await Translations.GetLanguage(Tester.Session, id, ct).Require();
                language.Languages.Should().BeEquivalentTo(expectedLanguages, "for #{0}", id);
                return language.Languages;
            },
            timeout ?? (TestRunnerInfo.IsBuildAgent() ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(30)));
    }
}
