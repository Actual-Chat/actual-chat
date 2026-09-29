using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class BubbleRegistryTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void GetAllTypeIdsShouldListBubblesNotRenderedYet()
    {
        // arrange
        var expected = BubbleRef.New<RecordButtonBubble>().ToString();

        // act
        var ids = BubbleRegistry.GetAllTypeIds();

        // assert
        ids.Should().Contain(expected);
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().HaveCountGreaterThan(5);
    }
}
