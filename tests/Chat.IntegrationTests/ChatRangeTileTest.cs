using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatRangeTileTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact(Timeout = 180_000)]
    public async Task SparseTileShouldNotExpandIntoItsNeighbors()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chat, _) = await tester.CreateAndGetChat(false, "range-tile-disjoint");
        var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();
        var tiles = Constants.Chat.ConversationIdTiles;
        var firstTile = tiles.GetTile(0);
        var secondTile = tiles.GetTile(tiles.TileSize);
        var entries = new List<ChatEntry>();
        while (entries.Count == 0 || entries[^1].LocalId < secondTile.Start + 5)
            entries.Add(await tester.CreateTextEntry(chat.Id, "entry"));
        // Few visible entries in the first tile: it used to expand into the next one to look "big enough".
        foreach (var entry in entries.Where(e => e.LocalId < firstTile.End - 5))
            await tester.RemoveTextEntry(entry.Id);

        // act
        var tile1 = await chatsBackend.GetChatRangeTile(chat.Id, firstTile.Start, CancellationToken.None);
        var tile2 = await chatsBackend.GetChatRangeTile(chat.Id, secondTile.Start, CancellationToken.None);

        // assert
        tile1.LidRange.Should().Be(firstTile.Range);
        tile2.LidRange.Should().Be(secondTile.Range);
        tile1.EntryLidRanges.Should().OnlyContain(r => firstTile.Range.Contains(r));
        tile2.EntryLidRanges.Should().OnlyContain(r => secondTile.Range.Contains(r));
        tile1.NextLidTileStart.Should().Be(secondTile.Start);
        tile2.PreviousLidTileStart.Should().Be(firstTile.Start);
        tile2.NextLidTileStart.Should().BeNull();
    }
}
