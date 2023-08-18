using ActualChat.Diff;

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

    private record Data(Symbol Id, string Name);

    private sealed record DataDiff : RecordDiff
    {
        public static readonly DataDiff Empty = new ();

        public Symbol? Id { get; init; }
        public string? Name { get; init; }
    }
}
