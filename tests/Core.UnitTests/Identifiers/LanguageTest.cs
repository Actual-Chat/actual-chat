namespace ActualChat.Core.UnitTests.Identifiers;

public class LanguageTest(ITestOutputHelper @out) : SymbolIdentifierTestBase<Language>(@out)
{
    public override Symbol[] ValidIdentifiers => [ "eN-Us", "UA", "rU" ];
    public override Symbol[] InvalidIdentifiers => [ "X", "~" ];
}
