namespace ActualChat.Core.UnitTests.Identifiers;

public class StreamIdTest(ITestOutputHelper @out) : SymbolIdentifierTestBase<StreamId>(@out)
{
    public override Symbol[] ValidIdentifiers => [ "1234ab-x", "1234ab-x-x", "abcdef-xyzxyz" ];
    public override Symbol[] InvalidIdentifiers => [ "x", "1234ab-", "xxx-x", "~guest~1" ];
}
