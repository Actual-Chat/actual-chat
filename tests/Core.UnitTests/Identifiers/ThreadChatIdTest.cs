namespace ActualChat.Core.UnitTests.Identifiers;

public class ThreadChatIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<ChatId>(@out)
{
    public override string[] ValidIdentifiers => new[] { "the-actual-one-1", "whatever-100-2", "p-admin1-admin2-148" }
        .Concat(Constants.Chat.SystemChatIdValues)
        .ToArray();
    public override string[] InvalidIdentifiers => [ "the-actual-one-1-x" ];
}
