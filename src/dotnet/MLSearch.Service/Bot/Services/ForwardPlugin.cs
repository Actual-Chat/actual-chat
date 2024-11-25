using ActualChat.Chat;

namespace ActualChat.MLSearch.Bot.Services;

internal interface IForwardPlugin
{
    Task ForwardResults(string summary, IReadOnlyList<string> links, string conversationId, CancellationToken cancellationToken = default);
}

internal sealed class ForwardPlugin(
    ICommander commander,
    UrlMapper urlMapper
): IForwardPlugin
{
    public async Task ForwardResults(
        string summary, IReadOnlyList<string> links, string conversationId, CancellationToken cancellationToken = default)
    {
        var chatId = ChatId.TryParse(conversationId, out var parsedChatId)
            ? parsedChatId
            : throw new InvalidOperationException("Malformed conversation id detected.");

        var botId = Constants.User.Sherlock.GetSherlockAuthorId(chatId);
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
