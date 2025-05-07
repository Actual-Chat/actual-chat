namespace ActualChat.Core.UnitTests.Identifiers;

public class UserIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<UserId>(@out)
{
    public override string[] ValidIdentifiers => [ "admin", "bobby93", "~guest15" ];
    public override string[] InvalidIdentifiers => [ "x", "some:one", "~", "~guest~1" ];
}
