namespace ActualChat.Core.UnitTests.Identifiers;

public class ChatIdTest(ITestOutputHelper @out) : SymbolIdentifierTestBase<ChatId>(@out)
{
    public override Symbol[] ValidIdentifiers => new Symbol[] { "1234abcd", "p-admin1-admin2", "whatever" }
        .Concat(Constants.Chat.SystemChatIds)
        .ToArray();
    public override Symbol[] InvalidIdentifiers => [ "x", "some:chat", "peer-chat", "~guest~1" ];
}
