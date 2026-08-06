namespace ActualChat.Core.UnitTests.Diff;

public class DiffEngineTest
{
    [Fact]
    public void ShouldDetectNoChange()
    {
        var source = new Data("1", "Jack");
        var target = new Data("1", "Jack");
        var diff = DiffEngine.Default.Diff<Data, DataDiff>(source, target);
        diff.Should().BeEquivalentTo(DataDiff.Empty);
    }

    [Fact]
    public void ShouldDetectNameChange()
    {
        // given
        var source = new Data("1", "Jack");
        var target = new Data("1", "John");

        // when
        var diff = DiffEngine.Default.Diff<Data, DataDiff>(source, target);

        // then
        diff.Should().BeEquivalentTo(new DataDiff { Name = "John" });
    }

    [Fact]
    public void ShouldDetectAllChanges()
    {
        // given
        var source = new Data("1", "Jack");
        var target = new Data("2", "John");

        // when
        var diff = DiffEngine.Default.Diff<Data, DataDiff>(source, target);

        // then
        diff.Should().BeEquivalentTo(new DataDiff { Id = "2", Name = "John" });
    }

    [Fact]
    public void DynamicPatchAppliesLeafFieldsOnDerivedType()
    {
        Animal source = new Dog("Rex", "Lab");
        var diff = new AnimalDiff { Name = "Buddy", Breed = "Poodle" };

        // Generic Patch uses the static base type Animal, so Breed (which lives only
        // on Dog) is not applied:
        var staticPatched = (Dog)DiffEngine.Default.Patch(source, diff);
        staticPatched.Name.Should().Be("Buddy");
        staticPatched.Breed.Should().Be("Lab");

        // DynamicPatch picks the handler from runtime types, so Breed is applied:
        var dynamicPatched = (Dog)DiffEngine.Default.DynamicPatch(source, diff)!;
        dynamicPatched.Name.Should().Be("Buddy");
        dynamicPatched.Breed.Should().Be("Poodle");
    }

    [Fact]
    public void DynamicPatchReturnsSourceWhenDiffIsNull()
    {
        Animal source = new Dog("Rex", "Lab");
        var patched = DiffEngine.Default.DynamicPatch(source, null);
        patched.Should().BeSameAs(source);
    }

    private sealed record Data(Symbol Id, string Name);

    private sealed record DataDiff : RecordDiff
    {
        public static readonly DataDiff Empty = new ();

        public Symbol? Id { get; init; }
        public string? Name { get; init; }
    }

    private abstract record Animal(string Name);

    private sealed record Dog(string Name, string Breed) : Animal(Name);

    private sealed record AnimalDiff : RecordDiff
    {
        public string? Name { get; init; }
        public string? Breed { get; init; }
    }
}
