using ActualChat.Diff;

namespace ActualChat.Core.UnitTests;

public class DiffTest : TestBase
{
    public DiffTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void BasicTest()
    {
        var engine = DiffEngine.Default;
        var animal1 = new Animal() {
            Name = "Mosya",
            AltName = "Штирлиц",
            LegCount = 4,
            Tags = ApiArray<Symbol>.Empty.Add("1").Add("2"),
        };
        var animal2 = animal1 with {
            Name = "Rosha",
            AltName = null,
            Tags = ApiArray<Symbol>.Empty.Add("2").Add("3"),
        };

        var diff = engine.Diff<Animal, AnimalDiff>(animal1, animal1);
        Out.WriteLine($"Diff 1: {diff}");
        diff.Name.Should().BeNull();
        diff.AltName.Should().Be(Option.None<string>());
        diff.LegCount.Should().Be(null);
        diff.Tags.AddedItems.Count.Should().Be(0);
        diff.Tags.RemovedItems.Count.Should().Be(0);

        diff = engine.Diff<Animal, AnimalDiff>(animal1, animal2);
        Out.WriteLine($"Diff 2: {diff}");
        var animal2a = engine.Patch(animal1, diff);
        animal2a.Should().NotBeSameAs(animal2);
        animal2a.Name.Should().Be(animal2.Name);
        animal2a.AltName.Should().Be(animal2.AltName);
        animal2a.LegCount.Should().Be(animal2.LegCount);
        animal2a.Tags.Should().BeEquivalentTo(animal2.Tags);
    }

    // Nested types

    public record Animal
    {
        public string Name { get; init; } = "";
        public string? AltName { get; init; }
        public int LegCount { get; init; }
        public ApiArray<Symbol> Tags { get; init; } = ApiArray<Symbol>.Empty;
    }

    public record AnimalDiff : RecordDiff
    {
        public string? Name { get; init; }
        public Option<string?> AltName { get; init; }
        public int? LegCount { get; init; }
        public SetDiff<ApiArray<Symbol>, Symbol> Tags { get; init; } =
            SetDiff<ApiArray<Symbol>, Symbol>.Unchanged;
    }
}
