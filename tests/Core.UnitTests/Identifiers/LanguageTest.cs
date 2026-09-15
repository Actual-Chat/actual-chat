namespace ActualChat.Core.UnitTests.Identifiers;

public class LanguageTest(ITestOutputHelper @out) : ObjectIdTestBase<Language>(@out)
{
    public override string[] ValidIdentifiers => [ "eN-Us", "UK", "rU" ];
    public override string[] InvalidIdentifiers => [ "X", "~", "max" ];
}
