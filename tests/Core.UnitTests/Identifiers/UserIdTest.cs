namespace ActualChat.Core.UnitTests.Identifiers;

public class UserIdTest(ITestOutputHelper @out) : SymbolIdentifierTestBase<UserId>(@out)
{
    public override Symbol[] ValidIdentifiers => [ "admin", "bobby93", "~guest15" ];
    public override Symbol[] InvalidIdentifiers => [ "x", "some:one", "~", "~guest~1" ];
}
