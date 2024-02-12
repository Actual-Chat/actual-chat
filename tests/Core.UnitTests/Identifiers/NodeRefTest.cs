namespace ActualChat.Core.UnitTests.Identifiers;

public class NodeRefTest(ITestOutputHelper @out) : SymbolIdentifierTestBase<NodeRef>(@out)
{
    public override Symbol[] ValidIdentifiers => [ "1234abcd", "1234abcde" ];
    public override Symbol[] InvalidIdentifiers => [ "x", "some:node", "x-no", "~wrong~ne" ];
}
