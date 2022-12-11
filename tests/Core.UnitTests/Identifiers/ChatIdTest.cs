namespace ActualChat.Core.UnitTests.Identifiers;

public class ChatIdTest : SymbolIdentifierTestBase<ChatId>
{
    public override Symbol[] ValidIdentifiers => new Symbol[] { "1234abcd", "p-admin1-admin2", "whatever" }
        .Concat(Constants.Chat.SystemChatIds)
        .ToArray();
    public override Symbol[] InvalidIdentifiers => new Symbol[] { "x", "some:chat", "peer-chat", "~guest~1" };

    public ChatIdTest(ITestOutputHelper @out) : base(@out) { }
}
