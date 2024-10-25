using System.ComponentModel;
using ActualChat.Chat;
using Microsoft.SemanticKernel;

namespace ActualChat.MLSearch.Bot.Services;

internal sealed class ForwardPlugin(
    ICommander commander,
    UrlMapper urlMapper
)
{
    [KernelFunction]
    [Description("Forward last search results to the user with a summary.")]
    public async Task ForwardResults(
        [Description("Search results summary.")] string summary,
        [Description("List of links to the relevant results.")] IReadOnlyList<string> links,
        [Description("ID of ongoing search conversation.")] string conversationId
    )
    {
        var cancellationToken = CancellationToken.None;
        var chatId = ChatId.TryParse(conversationId, out var parsedChatId)
            ? parsedChatId
            : throw new InvalidOperationException("Malformed conversation id detected.");

        AuthorId botId = new(chatId, Constants.User.Sherlock.AuthorLocalId, AssumeValid.Option);
        var textEntryId = new TextEntryId(chatId, 0, AssumeValid.Option);
        var upsertCommand = new ChatsBackend_ChangeEntry(
            textEntryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = botId,
                Content =
                    $"{summary}\n{ string.Join('\n', links.Select(e => new LocalUrl(e).ToAbsolute(urlMapper))) }",
            }));
        await commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
