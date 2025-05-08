using ActualChat.Testing.Host;
using ActualLab.Mathematics;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatEntryOperationsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IChats Chats => Tester.Chats;

    [Fact]
    public async Task ShouldNotHaveAccessTo()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var bob = await Tester.SignInAsUniqueBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);
        var textEntry = await Tester.CreateTextEntry(chatId, "Hello");
        await Tester.SignInAsBobAdmin();

        // act
        var getTile = Chats.GetTile(Tester.Session, chatId, ChatEntryKind.Text, new Range<long>(0, 4), CancellationToken.None).AsAsyncFunc();

        // assert
        await getTile.Should().ThrowAsync<NotFoundException<Chat>>();
    }
}
