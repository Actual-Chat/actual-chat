using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class LanguagePickerExtTest
{
    [Fact]
    public void PickerShouldKeepShowingASelectionItDoesNotOffer()
    {
        // act
        var withPreview = Languages.AllUI.WithSelected(Languages.Kannada);
        var withOffered = Languages.AllUI.WithSelected(Languages.English);
        var withAuto = Languages.AllUI.WithSelected(null);

        // assert
        withPreview.Should().Equal(Languages.AllUI.Append(Languages.Kannada));
        withOffered.Should().BeSameAs(Languages.AllUI);
        withAuto.Should().BeSameAs(Languages.AllUI);
    }
}
