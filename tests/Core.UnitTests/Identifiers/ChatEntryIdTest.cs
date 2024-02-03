namespace ActualChat.Core.UnitTests.Identifiers;

public class ChatEntryIdTest(ITestOutputHelper @out) : SymbolIdentifierTestBase<ChatEntryId>(@out)
{
    public override Symbol[] ValidIdentifiers => [
        "thisIsChatId:0:0",
        "thisIsChatId:1:0",
        "thisIsChatId:2:0",
        "thisIsChatId:2:10",
        "p-admin1-admin2:0:10",
        "p-admin1-admin2:1:100",
        "p-admin1-admin2:2:1000",
    ];
    public override Symbol[] InvalidIdentifiers => [
        "x:0:0",
        "thisIsChatId",
        "thisIsChatId:",
        "thisIsChatId::0",
        "thisIsChatId:0:",
        "thisIsChatId:-1:0",
        "thisIsChatId:9:0",
        "thisIsChatId:1:-1",
    ];
}
