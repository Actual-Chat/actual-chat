using Bunit;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ErrorBarrierBoundaryTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void ErrorContentShouldSurviveSeveralErrorsInOneRender(int failingChildCount)
    {
        // arrange
        using var context = TestBunitContext.New();
        var activations = new List<Exception>();

        // act
        var cut = context.Render<ErrorBarrierBoundary>(p => p
            .Add(x => x.Activated, activations.Add)
            .Add(x => x.ChildContent, b => AddFailingChildren(b, failingChildCount))
            .Add(x => x.ErrorContent, _ => b => b.AddContent(0, "failed")));

        // assert
        cut.WaitForAssertion(() => cut.Markup.Should().Be("failed",
            "the renderer's empty render queued for each later error must not wipe the error content"));
        activations.Select(e => e.Message).Should().Equal(["child 1"],
            "errors arriving while the boundary is already failed are the same activation");
    }

    [Fact]
    public void RecoveredBoundaryShouldReportTheNextErrorAsNewActivation()
    {
        // arrange
        using var context = TestBunitContext.New();
        var activations = new List<Exception>();
        var cut = context.Render<ErrorBarrierBoundary>(p => p
            .Add(x => x.Activated, activations.Add)
            .Add(x => x.ChildContent, b => AddFailingChildren(b, 2))
            .Add(x => x.ErrorContent, _ => b => b.AddContent(0, "failed")));
        cut.WaitForAssertion(() => cut.Markup.Should().Be("failed"));

        // act
        cut.InvokeAsync(cut.Instance.Recover);

        // assert
        cut.WaitForAssertion(() => activations.Should().HaveCount(2));
        cut.WaitForAssertion(() => cut.Markup.Should().Be("failed"));
    }

    // Private methods

    private static void AddFailingChildren(RenderTreeBuilder builder, int count)
    {
        for (var i = 1; i <= count; i++) {
            builder.OpenComponent<FailingChild>(0);
            builder.AddComponentParameter(1, nameof(FailingChild.Index), i);
            builder.SetKey(i);
            builder.CloseComponent();
        }
    }

    // Nested types

    private sealed class FailingChild : ComponentBase
    {
        [Parameter] public int Index { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => throw new InvalidOperationException($"child {Index}");
    }
}
