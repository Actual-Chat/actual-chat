using ActualChat.UI.Blazor.Components;

namespace ActualChat.UI.Blazor.UnitTests;

public class VirtualListDataTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void LastItemShouldResolveGroupLeafWhenGroupIsLast()
    {
        // arrange
        var leaf1 = new TestItem("10");
        var leaf2 = new TestItem("11");
        var data = new VirtualListData<TestItem>([new TestItem("1"), new TestGroup("10", [leaf1, leaf2])]);

        // act
        var lastItem = data.LastItem;

        // assert
        lastItem.Should().BeSameAs(leaf2);
    }

    [Fact]
    public void LastItemShouldResolveGroupLeafWhenTrailedBySkipKeyItem()
    {
        // arrange - the live block's shape: an author group closed by a skip-key footer
        var leaf1 = new TestItem("10");
        var leaf2 = new TestItem("11");
        var group = new TestGroup("10", [leaf1, leaf2]);
        var footer = new TestItem("footer") { ShouldSkipKey = true };
        var data = new VirtualListData<TestItem>([new TestItem("1"), group, footer]);

        // act
        var lastItem = data.LastItem;

        // assert
        lastItem.Should().BeSameAs(leaf2);
    }

    [Fact]
    public void FirstItemShouldResolveGroupLeafWhenPrecededBySkipKeyItem()
    {
        // arrange
        var leaf1 = new TestItem("10");
        var leaf2 = new TestItem("11");
        var group = new TestGroup("10", [leaf1, leaf2]);
        var header = new TestItem("header") { ShouldSkipKey = true };
        var data = new VirtualListData<TestItem>([header, group, new TestItem("20")]);

        // act
        var firstItem = data.FirstItem;

        // assert
        firstItem.Should().BeSameAs(leaf1);
    }

    [Fact]
    public void KeyRangeShouldSpanGroupLeavesWhenTrailedBySkipKeyItem()
    {
        // arrange
        var group = new TestGroup("10", [new TestItem("10"), new TestItem("11")]);
        var footer = new TestItem("footer") { ShouldSkipKey = true };
        var data = new VirtualListData<TestItem>([new TestItem("1"), group, footer]);

        // act
        var keyRange = data.KeyRange;

        // assert
        keyRange.Should().Be(new Range<string>("1", "11"));
    }

    // Nested types

    private class TestItem(string key) : IVirtualListItem
    {
        public string Key { get; } = key;
        public bool IsGroup => this is IVirtualListGroup;
        public bool ShouldSkipKey { get; init; }
    }

    private sealed class TestGroup(string key, IReadOnlyList<TestItem> items)
        : TestItem(key), IVirtualListGroup<TestItem>
    {
        public IReadOnlyList<TestItem> Items { get; } = items;
    }
}
