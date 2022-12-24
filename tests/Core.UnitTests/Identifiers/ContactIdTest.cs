namespace ActualChat.Core.UnitTests.Identifiers;

public class ContactIdTest : SymbolIdentifierTestBase<ContactId>
{
    public override Symbol[] ValidIdentifiers => new Symbol[] {
        "admin chatId1",
        "admin p-admin-bobby93",
        "bobby93 chatId1",
        "bobby93 p-admin-bobby93",
    };
    public override Symbol[] InvalidIdentifiers => new Symbol[] {
        "x",
        "x ",
        "x y",
        "x p-x-y",
        "admin p-x-y",
        "admin x",
        "admin p-bobby93-admin",
        "bobby93 x",
        "bobby93 -",
        "bobby93 p-bobby93-admin",
    };

    public ContactIdTest(ITestOutputHelper @out) : base(@out) { }
}
