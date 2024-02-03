namespace ActualChat.Core.UnitTests.Identifiers;

public class MeshNodeIdTest(ITestOutputHelper @out) : SymbolIdentifierTestBase<MeshNodeId>(@out)
{
    public override Symbol[] ValidIdentifiers => [ "1234abcd", "1234abcde" ];
    public override Symbol[] InvalidIdentifiers => [ "x", "some:node", "peer-no", "~wrong~ne" ];
}
