namespace ActualChat.Core.UnitTests.Identifiers;

public abstract class StringIdentifierTestBase<TIdentifier>(ITestOutputHelper @out) : TestBase(@out)
    where TIdentifier : StringIdentifier, IStringIdentifier<TIdentifier>
{
    public abstract string[] ValidIdentifiers { get; }
    public abstract string[] InvalidIdentifiers { get; }

    [Fact]
    public void ParseTest()
    {
        var parsed = ValidIdentifiers.Select(x => TIdentifier.TryParse(x)).ToArray();
        WriteLine(parsed.ToDelimitedString());
        parsed.All(id => id != null).Should().BeTrue();

        parsed = InvalidIdentifiers.Select(x => TIdentifier.TryParse(x)).ToArray();
        WriteLine(parsed.ToDelimitedString());
        parsed.All(id => id == null).Should().BeTrue();
    }

    [Fact]
    public void EqualityTest()
    {
        var identifiers = ValidIdentifiers.Select(TIdentifier.Parse).Concat([null]).ToArray();
        for (var i = 0; i < identifiers.Length; i++) {
            var id1 = identifiers[i];
            WriteLine($"{id1?.GetType().GetName()} '{id1}'");
            for (var j = 0; j < identifiers.Length; j++) {
                var id2 = identifiers[j];
                WriteLine($"== '{id2}' -> {Equals(id1, id2)}");
                Equals(id1, id2).Should().Be(Equals(i, j));
            }
        }
    }

    [Fact]
    public void SerializationTest()
    {
        var identifiers = ValidIdentifiers.Select(TIdentifier.Parse).Concat([null]).ToArray();
        foreach (var id in identifiers) {
            id.AssertPassesThroughSerializers(Out);

            var s1 = new NewtonsoftJsonSerializer();
            s1.Write(id).Should().Be(GetJson(id));
            s1.Write(new object?[] { id }).Should().Be($"[{GetJson(id)}]");
            var s2 = new SystemJsonSerializer(JsonSerializerOptions.Default);
            s2.Write(id).Should().Be(GetJson(id));
            s2.Write(new object?[] { id }).Should().Be($"[{GetJson(id)}]");
            var s3 = new SystemJsonSerializer(JsonSerializerOptions.Web);
            s3.Write(id).Should().Be(GetJson(id));
            s3.Write(new object?[] { id }).Should().Be($"[{GetJson(id)}]");
        }
        return;

        string GetJson(TIdentifier? id)
            => id == null ? "null" : $"\"{id.Value}\"";
    }

    [Fact]
    public void PropertyBagSerializationTest()
    {
        var identifiers = ValidIdentifiers.Select(TIdentifier.Parse).Concat([null]).ToArray();
        foreach (var id in identifiers) {
            var bag = new PropertyBag()
                .KeylessSet("s")
                .Set("x", id)
                .KeylessSet(id);
            bag = bag.PassThroughSerializers(Out);
            bag.KeylessGet<string>().Should().Be("s");
            bag.Get<TIdentifier>("x").Should().Be(id);
            bag.KeylessGet<TIdentifier>().Should().Be(id);
        }
    }

    [Fact]
    public void MutablePropertyBagSerializationTest()
    {
        var identifiers = ValidIdentifiers.Select(TIdentifier.Parse).Concat([null]).ToArray();
        foreach (var id in identifiers) {
            var bag = new MutablePropertyBag();
            bag.KeylessSet("s");
            bag.Set("x", id);
            bag.KeylessSet(id);
            bag = bag.PassThroughSerializers(Out);
            bag.KeylessGet<string>().Should().Be("s");
            bag.Get<TIdentifier>("x").Should().Be(id);
            bag.KeylessGet<TIdentifier>().Should().Be(id);
        }
    }
}
