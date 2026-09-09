using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Components;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallCardFormatTest
{
    [Theory]
    [InlineData(CallOutcome.NoAnswer, true, "icon-call-arrow-out", false)]
    [InlineData(CallOutcome.NoAnswer, false, "icon-call-arrow-in", true)]
    [InlineData(CallOutcome.Declined, true, "icon-call-arrow-out", false)]
    [InlineData(CallOutcome.Declined, false, "icon-call-arrow-in", false)]
    [InlineData(CallOutcome.Canceled, true, "icon-call-cross", false)]
    [InlineData(CallOutcome.Canceled, false, "icon-call-arrow-in", true)]
    [InlineData(CallOutcome.Ended, true, "icon-call-arrow-out", false)]
    [InlineData(CallOutcome.Ended, false, "icon-call-arrow-in", false)]
    // An outcome from a newer server: no arm of its own, so it falls back to the neutral card.
    [InlineData(CallOutcome.None, true, "icon-phone-call", false)]
    [InlineData(CallOutcome.None, false, "icon-phone-call", false)]
    public void EveryCellOfTheWordingTableShouldResolve(
        CallOutcome outcome, bool isCaller, string expectedIcon, bool expectedCallBack)
    {
        // arrange
        var l = NewLocalizer();

        // act
        var (icon, title, hint, isCallBack) = CallCardFormat.Get(outcome, isCaller, l);

        // assert
        icon.Should().Be(expectedIcon);
        title.Should().NotBeNullOrWhiteSpace();
        title.Should().NotContain("Call_Entry_", "the key must resolve, not render as itself");
        isCallBack.Should().Be(expectedCallBack);
        if (expectedCallBack)
            hint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void MissedAndOutgoingShouldReadDifferently()
    {
        // The two sides of one stored row must not collapse into the same sentence.
        // arrange
        var l = NewLocalizer();

        // act
        var caller = CallCardFormat.Get(CallOutcome.NoAnswer, true, l);
        var callee = CallCardFormat.Get(CallOutcome.NoAnswer, false, l);

        // assert
        caller.Title.Should().NotBe(callee.Title);
    }

    [Fact]
    public void AFinishedCallShouldNameItsDirection()
    {
        // The two sides of one finished call must not read as the same sentence either.
        // arrange
        var l = NewLocalizer();

        // act
        var caller = CallCardFormat.Get(CallOutcome.Ended, true, l);
        var callee = CallCardFormat.Get(CallOutcome.Ended, false, l);

        // assert
        caller.Title.Should().NotBe(callee.Title);
        caller.Icon.Should().NotBe(callee.Icon);
    }

    private static IStringLocalizer NewLocalizer()
        => new TestStringLocalizer(StringCatalogs.LoadStrings(Languages.English)!, Languages.English);
}
