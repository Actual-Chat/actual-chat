using ActualChat.Chat;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentArrangerTests(ITestOutputHelper @out) : TestBase(@out)
{
    private readonly string[] _messages = [
        "I've paid my dues",
        "Time after time",
        "I've done my sentence",
        "But committed no crime",
        "",
        "",
        "And bad mistakes-",
        "I've made a few",
        "I've had my share of sand kicked in my face",
        "But I've come through",
        "",
        "And I need to go on and on, and on, and on",
        "",
        "",
        "We are the champions, my friends",
        "And we'll keep on fighting 'til the end",
        "We are the champions",
        "We are the champions",
        "No time for losers",
        "'Cause we are the champions of the world",
        "",
        " ",
        "  ",
        "   ",
        " \r\n ",
        " \t ",
        "\n",
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ArrangerSkipsEmptyEntries(int maxEntriesPerDocument)
    {
        var entries = GetEntries(_messages).ToList();
        var emptyEntryIds = new HashSet<ChatEntryId>(
            from e in entries
            where string.IsNullOrEmpty(e.Content)
            select e.Id);
        var contentArranger = new ChatContentArranger(Mock.Of<IChatsBackend>()) {
            MaxEntriesPerDocument = maxEntriesPerDocument,
        };
        var sourceGroups = await contentArranger.ArrangeAsync(entries, [], CancellationToken.None).ToListAsync();
        Assert.True(sourceGroups.Count > 0);
        Assert.True(sourceGroups.All(se => se.Entries.Count > 0 && se.Entries.Count <= maxEntriesPerDocument));
        Assert.DoesNotContain(sourceGroups.SelectMany(se => se.Entries).Select(e => e.Id), emptyEntryIds.Contains);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ArrangerDoesntLoadTailOrReturnAnythingIfBufferIsEmpty(int maxEntriesPerDocument)
    {
        var tailDocuments = ChatContentTestHelpers.CreateDocuments();

        var chats = new Mock<IChatsBackend>();
        chats
            .Setup(x => x.GetTile(
                It.IsAny<ChatId>(),
                It.IsAny<ChatEntryKind>(),
                It.IsAny<ActualLab.Mathematics.Range<long>>(),
                It.Is<bool>(include => include == true),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<ChatTile>(new InvalidOperationException("Unexpected call.")));
        var contentArranger = new ChatContentArranger(chats.Object) {
            MaxEntriesPerDocument = maxEntriesPerDocument,
        };
        var sourceGroups = await contentArranger.ArrangeAsync([], tailDocuments, CancellationToken.None).ToListAsync();
        Assert.Empty(sourceGroups);
        sourceGroups = await contentArranger.ArrangeAsync([.. GetEntries([""])], tailDocuments, CancellationToken.None).ToListAsync();
        Assert.Empty(sourceGroups);


        chats.Verify(x => x.GetTile(
            It.IsAny<ChatId>(),
            It.IsAny<ChatEntryKind>(),
            It.IsAny<ActualLab.Mathematics.Range<long>>(),
            It.Is<bool>(include => include == true),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IEnumerable<ChatEntry> GetEntries(IEnumerable<string> messages)
    {
        var chatId = new ChatId(Generate.Option);
        var localId = 1L;
        var version = DateTime.Now.Ticks;
        foreach (var msg in messages) {
            var entryId = new ChatEntryId(chatId, ChatEntryKind.Text, localId++, AssumeValid.Option);
            yield return new ChatEntry(entryId, version++) {
                Content = msg,
            };
        }
    }
}
