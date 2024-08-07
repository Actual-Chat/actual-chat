using ActualChat.Chat;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentArranger2Tests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task ArrangeInto2Dialogs()
    {
        var authors = ChatContentArranger2Utils.CreateAuthors(ChatContentArranger2Utils.Messages);
        var entries = ChatContentArranger2Utils.CreateEntries(ChatContentArranger2Utils.Messages, authors).ToList();
        var authorsBackend = ChatContentArranger2Utils.CreateAuthorsBackend(authors.Values);
        var chatDialogFormatter = new ChatDialogFormatter(authorsBackend);

        var dialog1EntryIds = new long[] { 1, 2, 3, 6 };
        var dialog2EntryIds = new long[] { 4, 5 };
        var supposedDialogs = new[] { dialog1EntryIds, dialog2EntryIds };
        var dialogFragmentAnalyzer = CreateDialogFragmentAnalyzer(supposedDialogs, entries);
        var contentArranger = new ChatContentArranger2(
            Mock.Of<IChatsBackend>(),
            dialogFragmentAnalyzer,
            chatDialogFormatter);
        var sourceGroups = await contentArranger.ArrangeAsync(entries, [], CancellationToken.None).ToListAsync();
        sourceGroups.Count.Should().Be(2);
        sourceGroups[0].Entries.Select(c => c.Id.LocalId).Should().BeEquivalentTo(dialog1EntryIds);
        sourceGroups[1].Entries.Select(c => c.Id.LocalId).Should().BeEquivalentTo(dialog2EntryIds);
    }

    private IDialogFragmentAnalyzer CreateDialogFragmentAnalyzer(long[][] supposedDialogs, IList<ChatEntry> entries)
    {
        var mock = new Mock<IDialogFragmentAnalyzer>();
        mock
            .Setup(c => c.IsDialogAboutTheSameTopic(It.IsAny<string>()))
            .Returns<string>(d => {
                var lines = d.Split(Environment.NewLine);
                if (lines.Length == 1)
                    return Task.FromResult(Option<bool>.Some(true));

                foreach (var supposedDialog in supposedDialogs) {
                    if (supposedDialog.Length < lines.Length)
                        continue; // Longer than supposed dialog

                    var match = true;
                    for (int i = 0; i < Math.Min(lines.Length, supposedDialog.Length); i++) {
                        var entryId = supposedDialog[i];
                        var entry = entries[(int)entryId - 1];
                        if (!lines[i].Contains(entry.Content, StringComparison.Ordinal)) {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                        return Task.FromResult(Option<bool>.Some(true));
                }

                return Task.FromResult(Option<bool>.Some(false));
            });

        return mock.Object;
    }
}
