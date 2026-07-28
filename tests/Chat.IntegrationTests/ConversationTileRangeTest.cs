using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

// GetTile walks the range tile by tile, so it only ever accepts an exact tile: an arbitrary
// range would make it iterate until it runs out of memory.

[Collection(nameof(ChatCollection))]
public class ConversationTileRangeTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static TileStack<long> IdTileStack => Constants.Chat.ServerIdTileStack;
    private IConversationsBackend Conversations
        => field ??= AppHost.Services.GetRequiredService<IConversationsBackend>();

    [Fact]
    public async Task RefusesRangeSpanningEverything()
    {
        // arrange
        var chatId = ChatId.Parse(GroupChatId.New().Value);

        // act
        var act = () => Conversations.GetTile(chatId, new Range<long>(0, long.MaxValue), default);

        // assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1L, 2L)] // Too small to be a tile
    [InlineData(0L, 1_000_000L)] // Too large to be a tile
    [InlineData(3L, 1283L)] // Right size, misaligned start
    public async Task RefusesRangeThatIsNotATile(long start, long end)
    {
        // arrange
        var chatId = ChatId.Parse(GroupChatId.New().Value);

        // act
        var act = () => Conversations.GetTile(chatId, new Range<long>(start, end), default);

        // assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AcceptsLastLayerTile()
    {
        // arrange
        var chatId = ChatId.Parse(GroupChatId.New().Value);
        var tileRange = IdTileStack.LastLayer.GetTile(0L).Range;

        // act
        var conversations = await Conversations.GetTile(chatId, tileRange, default);

        // assert
        conversations.Should().BeEmpty();
    }
}
