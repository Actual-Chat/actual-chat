namespace ActualChat.Core.UnitTests.Identifiers;

public class ConversationIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<ConversationId>(@out)
{
    public override string[] ValidIdentifiers => new[] { "1234abcd:125", "p-admin1-admin2:0", "whatever:999999" }
        .Concat(Constants.Chat.SystemChatIdValues.Select(id => id + ":1"))
        .ToArray();
    public override string[] InvalidIdentifiers => [ "x", "some:chat", "peer-chat:1", "~guest~1:12" ];
}
