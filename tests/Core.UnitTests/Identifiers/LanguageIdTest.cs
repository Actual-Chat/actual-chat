namespace ActualChat.Core.UnitTests.Identifiers;

public class LanguageIdTest : SymbolIdentifierTestBase<LanguageId>
{
    public override Symbol[] ValidIdentifiers => new Symbol[] { "eN-Us", "UK", "rU" };
    public override Symbol[] InvalidIdentifiers => new Symbol[] { "X", "~" };

    public LanguageIdTest(ITestOutputHelper @out) : base(@out) { }
}
