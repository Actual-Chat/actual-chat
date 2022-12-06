namespace ActualChat.Core.UnitTests.Identifiers;

public class UserIdTest : SymbolIdentifierTestBase<UserId>
{
    public override Symbol[] ValidIdentifiers => new Symbol[] { "admin", "bobby93", "~guest15" };
    public override Symbol[] InvalidIdentifiers => new Symbol[] { "x", "some:one", "~", "~guest~1" };

    public UserIdTest(ITestOutputHelper @out) : base(@out) { }
}

public class ChatIdTest : SymbolIdentifierTestBase<ChatId>
{
    public override Symbol[] ValidIdentifiers => new Symbol[] { "1234abcd", "p-admin1-admin2", "whatever" };
    public override Symbol[] InvalidIdentifiers => new Symbol[] { "x", "some:chat", "peer-chat", "~guest~1" };

    public ChatIdTest(ITestOutputHelper @out) : base(@out) { }
}
