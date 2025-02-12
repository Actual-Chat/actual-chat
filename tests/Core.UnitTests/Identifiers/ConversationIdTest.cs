namespace ActualChat.Core.UnitTests.Identifiers;

public class ConversationIdTest(ITestOutputHelper @out) : SymbolIdentifierTestBase<ConversationId>(@out)
{
    public override Symbol[] ValidIdentifiers => new Symbol[] { "1234abcd:125", "p-admin1-admin2:0", "whatever:999999" }
        .Concat(Constants.Chat.SystemChatIds.Select(id => id + ":1"))
        .ToArray();
    public override Symbol[] InvalidIdentifiers => [ "x", "some:chat", "peer-chat:1", "~guest~1:12" ];
}
