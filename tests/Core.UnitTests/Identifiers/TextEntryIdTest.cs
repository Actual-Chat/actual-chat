namespace ActualChat.Core.UnitTests.Identifiers;

public class TextEntryIdTest : SymbolIdentifierTestBase<TextEntryId>
{
    public override Symbol[] ValidIdentifiers => new Symbol[] {
        "thisIsChatId:0:0",
        "thisIsChatId:0:10",
        "p-admin1-admin2:0:10",
        "p-admin1-admin2:0:100",
    };
    public override Symbol[] InvalidIdentifiers => new Symbol[] {
        "x:0:0",
        "x:1:0",
        "thisIsChatId",
        "thisIsChatId:",
        "thisIsChatId::0",
        "thisIsChatId:0:",
        "thisIsChatId:-1:0",
        "thisIsChatId:1:0",
        "thisIsChatId:2:0",
        "thisIsChatId:9:0",
        "thisIsChatId:0:-1",
        "thisIsChatId:0:-2",
    };

    public TextEntryIdTest(ITestOutputHelper @out) : base(@out) { }
}
