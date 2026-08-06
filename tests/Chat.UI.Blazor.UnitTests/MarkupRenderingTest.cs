using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Components.MarkupParts;
using Bunit;
using Bunit.TestDoubles;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class MarkupRenderingTest
{
    private const string Payload = "<img src=x onerror=alert(1)>\"'&";

    [Fact]
    public void BreakableWordEncodesSpecialCharacters()
    {
        // arrange
        using var context = new BunitContext();

        // act
        var cut = context.Render<BreakableWord>(p => p.Add(x => x.Text, Payload));

        // assert
        cut.FindAll("img").Should().BeEmpty();
        cut.Markup.Should().NotContain("<img");
        cut.Markup.Should().Contain("&lt;img");
        cut.Markup.Should().Contain("&amp;");
    }

    [Fact]
    public void BreakableWordInsertsWordBreaks()
    {
        // arrange
        using var context = new BunitContext();

        // act
        var cut = context.Render<BreakableWord>(p => p
            .Add(x => x.Text, "abcdefghijklmnopqrst")
            .Add(x => x.Interval, 10));

        // assert
        cut.Markup.Should().MatchRegex(@"^abcdefghij<wbr\s*/?>klmnopqrst$");
    }

    [Fact]
    public void BreakableWordKeepsShortTextUnbroken()
    {
        // arrange
        using var context = new BunitContext();

        // act
        var cut = context.Render<BreakableWord>(p => p
            .Add(x => x.Text, "abcdefghij")
            .Add(x => x.Interval, 10));

        // assert
        cut.Markup.Should().Be("abcdefghij");
    }

    [Fact]
    public void PrefixesSchemeOnWwwLink()
    {
        // act
        var paragraph = new MarkupParser().Parse("www.example.com:8080/path")
            .Should().BeOfType<ParagraphMarkup>().Subject;
        var markup = paragraph.Content.Should().BeOfType<UrlMarkup>().Subject;
        var href = UrlMarkupView.GetHrefUrl(markup);

        // assert
        href.Should().Be("https://www.example.com:8080/path");
    }

    [Fact]
    public void AlertConfirmationInfoEncodesTarget()
    {
        // arrange
        using var context = new BunitContext();

        // act
        var cut = context.Render<AlertConfirmationInfo>(p => p
            .Add(x => x.Target, Payload)
            .Add(x => x.TargetClass, "font-bold"));

        // assert
        cut.FindAll("img").Should().BeEmpty();
        cut.Markup.Should().NotContain("<img");
        cut.Markup.Should().Contain("&lt;img");
    }

    [Fact]
    public void ConfirmModalRendersAdditionalContentAsMarkup()
    {
        // arrange
        using var context = new BunitContext();
        context.ComponentFactories.AddStub<DialogFrame>();
        var model = new ConfirmModal.Model(false, "Confirm?", () => { }) {
            AdditionalContent = builder => builder.AddContent(0, Payload),
        };

        // act
        var cut = context.Render<ConfirmModal>(p => p.Add(x => x.ModalModel, model));
        var body = cut.FindComponent<Stub<DialogFrame>>().Instance.Parameters.Get(x => x.Body);
        var bodyCut = context.Render(body!);

        // assert
        bodyCut.FindAll("img").Should().BeEmpty();
        bodyCut.Markup.Should().NotContain("<img");
        bodyCut.Markup.Should().Contain("&lt;img");
    }
}
