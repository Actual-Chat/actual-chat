using ActualChat.Testing.Host;

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
        var getTile = Chats.GetTile(Tester.Session, chatId, new Range<long>(0, 4), CancellationToken.None).AsAsyncFunc();

        // assert
        await getTile.Should().ThrowAsync<NotFoundException<Chat>>();
    }

    [Fact]
    public async Task ListEntriesShouldMatchGetEntryAcrossTiles()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(true);
        var (otherChatId, _) = await Tester.CreateChat(true);
        var entries = new List<ChatEntry>();
        for (var i = 0; i < 12; i++)
            entries.Add(await Tester.CreateTextEntry(chatId, $"entry {i}"));
        var otherEntry = await Tester.CreateTextEntry(otherChatId, "other chat");
        await Tester.RemoveTextEntry(entries[3].Id);
        var backend = Tester.AppServices.GetRequiredService<IChatsBackend>();
        ChatEntryId[] entryIds = [
            entries[11].Id, entries[0].Id, entries[3].Id,
            ChatEntryId.New(chatId, entries[11].LocalId + 1000), entries[0].Id, entries[6].Id,
        ];

        // act
        var result = await backend.ListEntries(entryIds);

        // assert
        result.Should().HaveCount(entryIds.Length);
        for (var i = 0; i < entryIds.Length; i++) {
            var expected = await backend.GetEntry(entryIds[i]);
            result[i]?.Id.Should().Be(expected?.Id);
            (result[i] is null).Should().Be(expected is null);
        }
        result[2].Should().BeNull("a removed entry isn't returned");
        result[3].Should().BeNull("a missing entry isn't returned");
        var withRemoved = await backend.ListEntries([entries[3].Id], true);
        withRemoved.Single()!.IsRemoved.Should().BeTrue();
        await FluentActions.Awaiting(() => backend.ListEntries([entries[0].Id, otherEntry.Id]))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
