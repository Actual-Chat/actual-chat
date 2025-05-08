namespace ActualChat.Core.UnitTests.Identifiers;

public class ChatEntryIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<ChatEntryId>(@out)
{
    public override string[] ValidIdentifiers => [
        "thisIsChatId:0:0",
        "thisIsChatId:1:0",
        "p-admin1-admin2:0:10",
        "p-admin1-admin2:1:100",

    ];
    public override string[] InvalidIdentifiers => [
        "x:0:0",
        "thisIsChatId",
        "thisIsChatId:",
        "thisIsChatId::0",
        "thisIsChatId:0:",
        "thisIsChatId:-1:0",
        "thisIsChatId:9:0",
        "thisIsChatId:1:-1",
        "thisIsChatId:2:0", // Video chat entries are invalid so far.
        "thisIsChatId:2:10",
        "p-admin1-admin2:2:1000",
    ];
}
