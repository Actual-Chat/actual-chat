namespace ActualChat.Core.UnitTests;

public class DiffTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var engine = DiffEngine.Default;
        var animal1 = new Animal() {
            Name = "Mosya",
            AltName = "Штирлиц",
            LegCount = 4,
            Tags = new Symbol[] {"1" }.With("2"),
        };
        var animal2 = animal1 with {
            Name = "Rosha",
            AltName = null,
            Tags = new Symbol[] {"2" }.With("3"),
        };

        var diff = engine.Diff<Animal, AnimalDiff>(animal1, animal1);
        WriteLine($"Diff 1: {diff}");
        diff.Name.Should().BeNull();
        diff.AltName.Should().Be(Option.None<string?>());
        diff.LegCount.Should().Be(null);
        diff.Tags.AddedItems.Length.Should().Be(0);
        diff.Tags.RemovedItems.Length.Should().Be(0);

        diff = engine.Diff<Animal, AnimalDiff>(animal1, animal2);
        WriteLine($"Diff 2: {diff}");
        var animal2a = engine.Patch(animal1, diff);
        animal2a.Should().NotBeSameAs(animal2);
        animal2a.Name.Should().Be(animal2.Name);
        animal2a.AltName.Should().Be(animal2.AltName);
        animal2a.LegCount.Should().Be(animal2.LegCount);
        animal2a.Tags.Should().BeEquivalentTo(animal2.Tags);
    }

    [Fact]
    public void SetDiffTest()
    {
        var rnd = new Random();
        var authorIds = Enumerable.Range(0, 50)
            .Select(i => AuthorId.New(ChatId.Parse("chatid"), i))
            .ToArray();
        var engine = DiffEngine.Default;
        for (var count = 2; count < 20; count++) {
            for (var i = 0; i < 100; i++) {
                var set1 = authorIds.Shuffle().Take(rnd.Next(count)).ToArray();
                var set2 = authorIds.Shuffle().Take(rnd.Next(count)).ToArray();
                var diff = engine.Diff<AuthorId[], SetDiff<AuthorId[], AuthorId>>(set1, set2);
                var set2a = engine.Patch(set1, diff);
                if (set2a.Length != set2.Length)
                    Assert.Fail();
                if (!set2a.All(x => set2.Contains(x)))
                    Assert.Fail();
            }
        }
    }

    // Nested types

    public sealed record Animal
    {
        public string Name { get; init; } = "";
        public string? AltName { get; init; }
        public int LegCount { get; init; }
        public Symbol[] Tags { get; init; } = [];
    }

    public sealed record AnimalDiff : RecordDiff
    {
        public string? Name { get; init; }
        public Option<string?> AltName { get; init; }
        public int? LegCount { get; init; }
        public SetDiff<Symbol[], Symbol> Tags { get; init; }
    }
}
