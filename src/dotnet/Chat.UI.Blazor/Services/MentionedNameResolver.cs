using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class MentionedNameResolver
{
    private ChatUI ChatUI { get;  }
    private IChatAuthors ChatAuthors { get; }
    private IAccounts Accounts { get; }
    public Session Session { get; }

    public MentionedNameResolver(ChatUI chatUI, IChatAuthors chatAuthors, IAccounts accounts, Session session)
    {
        ChatUI = chatUI;
        ChatAuthors = chatAuthors;
        Accounts = accounts;
        Session = session;
    }

    public Task<string> GetName(MentionKind mentionKind, string id, CancellationToken cancellationToken)
        => mentionKind switch {
            MentionKind.AuthorId => GetAuthorName(id, cancellationToken),
            MentionKind.UserId => GetUserName(id, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(mentionKind), mentionKind, null),
        };

    public async Task<string> GetUserName(string id, CancellationToken cancellationToken)
    {
        var author = await Accounts.GetUserAuthor(id, cancellationToken).Require().ConfigureAwait(false);
        return author.Name;
    }

    public async Task<string> GetAuthorName(string id, CancellationToken cancellationToken)
    {
        var author = await ChatAuthors.GetAuthor(Session, ChatUI.ActiveChatId.Value, id, true, cancellationToken)
            .Require()
            .ConfigureAwait(false);
        return author.Name;
    }
}
