namespace ActualChat.Core.UnitTests.Identifiers;

public class LanguageTest(ITestOutputHelper @out) : StringIdentifierTestBase<Language>(@out)
{
    public override string[] ValidIdentifiers => [ "eN-Us", "UA", "rU" ];
    public override string[] InvalidIdentifiers => [ "X", "~" ];
}
