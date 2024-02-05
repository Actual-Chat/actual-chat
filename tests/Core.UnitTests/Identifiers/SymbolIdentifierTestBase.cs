namespace ActualChat.Core.UnitTests.Identifiers;

public abstract class SymbolIdentifierTestBase<TIdentifier>(ITestOutputHelper @out) : TestBase(@out)
    where TIdentifier : struct, ISymbolIdentifier<TIdentifier>
{
    public abstract Symbol[] ValidIdentifiers { get; }
    public abstract Symbol[] InvalidIdentifiers { get; }
    public TIdentifier Default = default;
    public TIdentifier None = TIdentifier.None;

    [Fact]
    public void NoneTest()
    {
        None.Should().Be(Default);
        None.IsNone.Should().BeTrue();

        Default.Should().Be(None);
        Default.IsNone.Should().BeTrue();
    }

    [Fact]
    public void ParseTest()
    {
        var parsed = ValidIdentifiers.Select(s => TIdentifier.ParseOrNone(s)).ToArray();
        Out.WriteLine(parsed.ToDelimitedString());
        parsed.All(id => id.IsNone).Should().BeFalse();

        parsed = InvalidIdentifiers.Select(s => TIdentifier.ParseOrNone(s)).ToArray();
        Out.WriteLine(parsed.ToDelimitedString());
        parsed.All(id => id.IsNone).Should().BeTrue();
    }

    [Fact]
    public void EqualityTest()
    {
        var identifiers = ValidIdentifiers.Select(s => TIdentifier.Parse(s)).Concat(new[] { None }).ToArray();
        for (var i = 0; i < identifiers.Length; i++) {
            var id1 = identifiers[i];
            Out.WriteLine($"{id1.GetType().GetName()} '{id1}'");
            for (var j = 0; j < identifiers.Length; j++) {
                var id2 = identifiers[j];
                Out.WriteLine($"== '{id2}' -> {Equals(id1, id2)}");
                Equals(id1, id2).Should().Be(Equals(i, j));
            }
        }
    }

    [Fact]
    public void SerializationTest()
    {
        var identifiers = ValidIdentifiers.Select(s => TIdentifier.Parse(s)).Concat(new[] { None }).ToArray();
        foreach (var id in identifiers) {
            id.AssertPassesThroughAllSerializers(Out);
            var s1 = new NewtonsoftJsonSerializer();
            s1.Write(id).Should().Be($"\"{id.Value}\"");
            var s2 = new SystemJsonSerializer();
            s2.Write(id).Should().Be($"\"{id.Value}\"");
        }
    }
}
