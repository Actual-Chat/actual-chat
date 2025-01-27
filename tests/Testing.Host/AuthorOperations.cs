using ActualChat.Chat;

namespace ActualChat.Testing.Host;

public static class AuthorOperations
{
    public static Task PromoteToOwner(this IWebTester tester, AuthorId authorId)
        => tester.Commander.Call(new Authors_PromoteToOwner(tester.Session, authorId));
}
