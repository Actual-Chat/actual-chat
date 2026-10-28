using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ReactionsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IReactions Reactions => Tester.AppServices.GetRequiredService<IReactions>();

    [Fact]
    public async Task UnsupportedEmojiReactionShouldBeRejected()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chat, _) = await Tester.CreateAndGetChat(isPublicChat: true);
        var entry = await Tester.CreateTextEntry(chat.Id, "Hello");
        var unsupportedEmoji = Emoji.ParseOrUnsupported("🦄");

        // act
        var react = () => Tester.React(entry.Id, unsupportedEmoji);

        // assert
        await react.Should().ThrowAsync<Exception>();
        var summaries = await Reactions.ListSummaries(Tester.Session, entry.Id, CancellationToken.None);
        summaries.Should().BeEmpty();
    }
}
