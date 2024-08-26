using ActualChat.Chat;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

namespace ActualChat.MLSearch.IntegrationTests.Indexing.ChatContent;

public class ChatContentArranger2Tests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact(Skip = "Run explicitly")]
    public async Task ArrangeInto2Dialogs()
    {
        var authors = ChatContentArranger2Utils.CreateAuthors(ChatContentArranger2Utils.Messages);
        var entries = ChatContentArranger2Utils.CreateEntries(ChatContentArranger2Utils.Messages, authors).ToList();
        var authorsBackend = ChatContentArranger2Utils.CreateAuthorsBackend(authors.Values);
        var chatDialogFormatter = new ChatDialogFormatter(authorsBackend);
        var contentArranger = new ChatContentArranger2(
            Mock.Of<IChatsBackend>(),
            new DialogFragmentAnalyzer(DialogFragmentAnalyzer.Options.Default, Mock.Of<ILogger>()),
            chatDialogFormatter);
        var sourceGroups = await contentArranger.ArrangeAsync(entries, [], CancellationToken.None).ToListAsync();
        sourceGroups.Count.Should().Be(2);

        // var dialog1 = await chatDialogFormatter.BuildUpDialog(sourceGroups[0].Entries);
        // var dialog2 = await chatDialogFormatter.BuildUpDialog(sourceGroups[1].Entries);

        sourceGroups[0].Entries.Select(c => c.Id.LocalId).Should().BeEquivalentTo(new long[] { 1, 2, 3, 6 });
        sourceGroups[1].Entries.Select(c => c.Id.LocalId).Should().BeEquivalentTo(new long[] { 4, 5 });
    }
}
