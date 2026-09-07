using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Components;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallCardFormatTest
{
    [Theory]
    [InlineData(CallOutcome.NoAnswer, true, "icon-call-out", false)]
    [InlineData(CallOutcome.NoAnswer, false, "icon-phone-missed", true)]
    [InlineData(CallOutcome.Declined, true, "icon-phone-off", false)]
    [InlineData(CallOutcome.Declined, false, "icon-phone-off", false)]
    [InlineData(CallOutcome.Canceled, true, "icon-phone-off", false)]
    [InlineData(CallOutcome.Canceled, false, "icon-phone-missed", true)]
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

    private static IStringLocalizer NewLocalizer()
        => new TestStringLocalizer(StringCatalogs.LoadStrings(Languages.English)!, Languages.English);
}
