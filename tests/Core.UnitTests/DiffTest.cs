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

    [Fact]
    public void SetDiffTest()
    {
        var rnd = new Random();
        var authorIds = Enumerable.Range(0, 50)
            .Select(i => new AuthorId(new ChatId("chatid"), i, AssumeValid.Option))
            .ToArray();
        var engine = DiffEngine.Default;
        for (var count = 2; count < 20; count++) {
            for (var i = 0; i < 100; i++) {
                var set1 = authorIds.Shuffle().Take(rnd.Next(count)).ToApiArray();
                var set2 = authorIds.Shuffle().Take(rnd.Next(count)).ToApiArray();
                var diff = engine.Diff<ApiArray<AuthorId>, SetDiff<ApiArray<AuthorId>, AuthorId>>(set1, set2);
                var set2a = engine.Patch(set1, diff);
                if (set2a.Count != set2.Count)
                    Assert.Fail();
                if (!set2a.All(x => set2.Contains(x)))
                    Assert.Fail();
            }
        }
    }

    // Nested types

    public record Animal
    {
        public string Name { get; init; } = "";
        public string? AltName { get; init; }
        public int LegCount { get; init; }
        public ApiArray<Symbol> Tags { get; init; }
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
