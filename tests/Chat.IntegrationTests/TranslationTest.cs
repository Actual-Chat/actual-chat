using System.Diagnostics.CodeAnalysis;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(TranslationCollection))]
public class TranslationTest(TranslationCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [field: AllowNull, MaybeNull]
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private IChats Chats => Tester.Chats;
    private ITranslations Translations => Tester.Translations;

    [Theory]
    [InlineData("Hi! How are you?", "en")]
    [InlineData("Привет! Как дела?", "ru")]
    [InlineData("Merhaba! Nasılsın?", "tr")]
    [InlineData("Hola, cómo estás?", "es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", "en,ru,tr,es")]
    public async Task ShouldDetectLanguageOnInsert(string text, string sExpectedLanguages)
    {
        if (TestRunnerInfo.IsBuildAgent())
            return; // only for local runs for now

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false, "translation lab");

        // act
        var entry = await Tester.CreateTextEntry(chatId, text);
        entry.Content.Should().Be(text);

        // assert
        await WhenDetected(entry.Id, sExpectedLanguages);
    }

    [Theory]
    [InlineData("Hi! How are you?", "en")]
    [InlineData("Привет! Как дела?", "ru")]
    [InlineData("Merhaba! Nasılsın?", "tr")]
    [InlineData("Hola, cómo estás?", "es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", "en,ru,tr,es")]
    public async Task ShouldDetectLanguageOnUpdate(string updatedText, string sExpectedLanguages)
    {
        if (TestRunnerInfo.IsBuildAgent())
            return; // only for local runs for now

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false, "translation lab");

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Some text");
        await Tester.UpdateTextEntry(entry.Id, updatedText);

        // assert
        await WhenDetected(entry.Id, sExpectedLanguages);
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

    private Task<ApiArray<Language>> WhenDetected(ChatEntryId entryId, string sExpectedLanguages)
    {
        var expectedLanguages = sExpectedLanguages.Split([',']).Select(Language.Parse).ToList();
        return ComputedTest.When(async ct => {
                var retrievedEntry = await Chats.GetEntry(Tester.Session, entryId, ct).Require();
                retrievedEntry.Languages.Should().BeEquivalentTo(expectedLanguages);
                return retrievedEntry.Languages;
            },
            TimeSpan.FromSeconds(10));
    }
}
