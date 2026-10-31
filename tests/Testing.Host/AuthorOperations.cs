
namespace ActualChat.Testing.Host;

public static class AuthorOperations
{
    public static Task PromoteToOwner(this IWebTester tester, AuthorId authorId)
        => tester.Commander.Call(new Authors_PromoteToOwner { Session = tester.Session, AuthorId = authorId });

    public static Task<Author?> GetAuthor(this IWebTester tester, AuthorId authorId, CancellationToken cancellationToken = default)
        => tester.Authors.Get(tester.Session, authorId.ChatId, authorId, cancellationToken);

    public static Task<AuthorFull?> GetOwnAuthor(this IWebTester tester, ChatId chatId, CancellationToken cancellationToken = default)
        => tester.Authors.GetOwn(tester.Session, chatId, cancellationToken);
}
