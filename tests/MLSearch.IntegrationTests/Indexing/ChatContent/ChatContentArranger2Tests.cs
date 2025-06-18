using ActualChat.AI;
using ActualChat.Chat;
using ActualChat.Chat.ML;
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
        var chatDialogFormatter = new ChatDialogFormatter(new DefaultAuthorNameRetriever(authorsBackend));
        var contentArranger = new ChatContentArranger2(
            Mock.Of<IChatsBackend>(MockBehavior.Loose),
            new DialogFragmentAnalyzer(
                DialogFragmentAnalyzer.Options.Default,
                Mock.Of<ILogger<DialogFragmentAnalyzer>>(MockBehavior.Loose),
                Mock.Of<IPromptHelpers>(MockBehavior.Loose),
                Mock.Of<IAnthropicClient>(MockBehavior.Loose)),
            chatDialogFormatter);
        var sourceGroups = await contentArranger.Arrange(entries, [], CancellationToken.None).ToListAsync();
        sourceGroups.Count.Should().Be(2);

        // var dialog1 = await chatDialogFormatter.BuildUpDialog(sourceGroups[0].Entries);
        // var dialog2 = await chatDialogFormatter.BuildUpDialog(sourceGroups[1].Entries);

        sourceGroups[0].Entries.Select(c => c.Id.LocalId).Should().BeEquivalentTo(new long[] { 1, 2, 3, 6 });
        sourceGroups[1].Entries.Select(c => c.Id.LocalId).Should().BeEquivalentTo(new long[] { 4, 5 });
    }
}
