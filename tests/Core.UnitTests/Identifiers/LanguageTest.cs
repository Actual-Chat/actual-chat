namespace ActualChat.Core.UnitTests.Identifiers;

public class LanguageTest : SymbolIdentifierTestBase<Language>
{
    public override Symbol[] ValidIdentifiers => new Symbol[] { "eN-Us", "UA", "rU" };
    public override Symbol[] InvalidIdentifiers => new Symbol[] { "X", "~" };

    public LanguageTest(ITestOutputHelper @out) : base(@out) { }
}
