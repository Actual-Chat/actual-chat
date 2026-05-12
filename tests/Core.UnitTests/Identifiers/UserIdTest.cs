namespace ActualChat.Core.UnitTests.Identifiers;

public class UserIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<UserId>(@out)
{
    public override string[] ValidIdentifiers => [ "admin", "actual-admin", "bobby93", "ml-search", "~guest15" ];
    public override string[] InvalidIdentifiers => [ "x", "some-one", "some_one", "some:one", "~", "~guest~1" ];
}
