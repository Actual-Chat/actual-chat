using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

/// <summary>
/// A member of a public place chat has no author row of its own - the author is derived from the
/// place-root one. Anything that resolves authors to decide what to show has to handle that.
/// </summary>
[Collection(nameof(ChatCollection))]
public sealed class PlaceChatAuthorVisibilityTest(
    ChatCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Owner => field ??= AppHost.NewWebClientTester(Out);
    private IChatsBackend Backend => field ??= Owner.AppServices.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend Authors => field ??= Owner.AppServices.GetRequiredService<IAuthorsBackend>();

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task PlaceMemberMessagesShouldStayVisibleInAPublicPlaceChat()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true);
        var (chatId, _) = await Owner.CreateChat(c => c with {
            Title = "Public place chat", IsPublic = true, PlaceId = place.Id,
        });
        await using var member = AppHost.NewWebClientTester(Out);
        var bob = await member.SignInAsUniqueBob();
        await member.JoinPlace(place.Id);

        // act
        var entry = await member.CreateTextEntry(chatId, "from a place member");

        // assert
        (await Authors.Exists(chatId, entry.AuthorId, default)).Should().BeTrue();
        (await Backend.GetEntry(entry.Id, default))!.Content.Should().Be("from a place member");
        var range = Constants.Chat.EntryIdTiles.GetTile(entry.LocalId).Range;
        var tile = await Backend.GetTile(chatId, range, true, default);
        tile.Entries.Should().Contain(e => e.LocalId == entry.LocalId);
        var newEntries = await Backend.ListNewEntries(chatId, entry.LocalId - 1, 10, default);
        newEntries.Should().Contain(e => e.LocalId == entry.LocalId);
        bob.Id.Should().NotBe(default(UserId));
    }
}
