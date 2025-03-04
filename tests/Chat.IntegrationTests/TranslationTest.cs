using System.Diagnostics.CodeAnalysis;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class TranslationTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [field: AllowNull, MaybeNull]
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private IChats Chats => Tester.Chats;

    [Theory]
    [InlineData("Hi! How are you?", "en")]
    [InlineData("Привет! Как дела?", "ru")]
    [InlineData("Merhaba! Nasılsın?", "tr")]
    [InlineData("Hola, cómo estás?", "es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", "en,ru,tr,es")]
    public async Task ShouldDetectLanguageOnInsert(string text, string sExpectedLanguages)
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false, "translation lab");

        // act
        var entry = await Tester.CreateTextEntry(chatId, text);
        var retrievedEntry = await Chats.GetEntry(Tester.Session, entry.Id);

        // assert
        var expectedLanguages = sExpectedLanguages.Split([',']).Select(Language.Parse).ToList();
        entry.Languages.Should().BeEquivalentTo(expectedLanguages);
        entry.Content.Should().Be(text);
        retrievedEntry.Should().BeEquivalentTo(entry, o => o.Excluding(x => x.BeginsAt));
    }

    [Theory]
    [InlineData("Hi! How are you?", "en")]
    [InlineData("Привет! Как дела?", "ru")]
    [InlineData("Merhaba! Nasılsın?", "tr")]
    [InlineData("Hola, cómo estás?", "es")]
    [InlineData("Hi! How are you? Привет! Как дела? Merhaba! Nasılsın? Hola, cómo estás?", "en,ru,tr,es")]
    public async Task ShouldDetectLanguageOnUpdate(string updatedText, string sExpectedLanguages)
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false, "translation lab");

        // act
        var entry = await Tester.CreateTextEntry(chatId, "Some text");
        var updatedEntry = await Tester.UpdateTextEntry(entry.Id, updatedText);
        var retrievedUpdatedEntry = await Chats.GetEntry(Tester.Session, entry.Id);

        // assert
        var expectedLanguages = sExpectedLanguages.Split([',']).Select(Language.Parse).ToList();
        updatedEntry.Languages.Should().BeEquivalentTo(expectedLanguages);
        updatedEntry.Content.Should().Be(updatedText);
        retrievedUpdatedEntry.Should().BeEquivalentTo(updatedEntry, o => o.Excluding(x => x.BeginsAt));
    }
}
