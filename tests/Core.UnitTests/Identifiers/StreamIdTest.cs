namespace ActualChat.Core.UnitTests.Identifiers;

public class StreamIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<StreamId>(@out)
{
    public override string[] ValidIdentifiers => [ "1234ab-x", "1234ab-x-x", "abcdef-xyzxyz" ];
    public override string[] InvalidIdentifiers => [ "x", "1234ab-", "xxx-x", "~guest~1" ];
}
