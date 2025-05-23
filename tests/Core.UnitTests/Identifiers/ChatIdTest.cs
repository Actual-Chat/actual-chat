namespace ActualChat.Core.UnitTests.Identifiers;

public class ChatIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<ChatId>(@out)
{
    public override string[] ValidIdentifiers => new[] { "1234abcd", "p-admin1-admin2", "whatever" }
        .Concat(Constants.Chat.SystemChatIdValues)
        .ToArray();
    public override string[] InvalidIdentifiers => [ "x", "some:chat", "peer-chat", "~guest~1" ];
}
