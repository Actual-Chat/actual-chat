using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class TranslationDubTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact(Timeout = 60_000)]
    public async Task ANewContentClearsTheDubAndDeletesItsMedia()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var commander = services.Commander();
        var translations = services.GetRequiredService<ITranslationsBackend>();
        var mediaBackend = services.GetRequiredService<IMediaBackend>();
        var entry = await Tester.CreateTextEntry(chatId, "Привет");
        var id = TranslationId.New(entry.Id, Languages.English);
        var translation = await ComputedTest.When(
            ct => translations.Get(id, translateIfMissing: true, ct).Require(),
            TimeSpan.FromSeconds(10));
        var mediaId = await Tester.SaveTextFile(chatId, "dub.webm", "fake dub audio");
        translation = await commander.Call(new TranslationsBackend_Change(id, translation!.Version,
            Change.Update(new TranslationDiff {
                DubMediaId = mediaId,
                DubContentHash = ChatEntryHashExt.GetContentHashString(translation.Content),
            })));
        translation!.HasValidDub().Should().BeTrue();

        // act
        translation = await commander.Call(new TranslationsBackend_Change(id, translation.Version,
            Change.Update(new TranslationDiff {
                Content = "Hello there",
                SourceContentHash = ChatEntryHashExt.GetContentHashString("Привет там"),
            })));

        // assert
        translation!.DubMediaId.Should().BeNull("a dub of the old content must not be served");
        translation.DubContentHash.IsNone.Should().BeTrue();
        var media = await mediaBackend.Get(mediaId, CancellationToken.None);
        media.Should().BeNull("the orphaned media is deleted with the change");
    }
}
