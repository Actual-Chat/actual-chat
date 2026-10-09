using ActualChat.Flows;
using MudBlazor;

namespace ActualChat.Mui.UnitTests;

public class FlowsFilterTest
{
    [Fact]
    public void EmptyFilterListsNoRows()
    {
        // arrange
        var filter = new FlowsFilter();

        // act
        var query = filter.ToQuery();

        // assert
        filter.ShowRows.Should().BeFalse();
        query.Limit.Should().Be(0);
        query.HideCompleted.Should().BeTrue();
    }

    [Theory]
    [InlineData("MyFlow", false)]
    [InlineData(null, true)]
    public void TypeOrProblematicFilterListsRows(string? typeName, bool problematicOnly)
    {
        // arrange
        var filter = new FlowsFilter(typeName, problematicOnly);

        // act
        var query = filter.ToQuery();

        // assert
        filter.ShowRows.Should().BeTrue();
        query.Limit.Should().Be(FlowsFilter.RowLimit);
        query.Name.Should().Be(typeName);
        query.ProblematicOnly.Should().Be(problematicOnly);
    }

    [Theory]
    [InlineData(FlowStatus.Failed, Color.Error)]
    [InlineData(FlowStatus.Stuck, Color.Warning)]
    [InlineData(FlowStatus.Completed, Color.Success)]
    [InlineData(FlowStatus.Idle, Color.Default)]
    [InlineData(FlowStatus.Scheduled, Color.Default)]
    public void StatusColorReflectsSeverity(FlowStatus status, Color expected)
        => FlowsFilter.GetStatusColor(status).Should().Be(expected);
}
