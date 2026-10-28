using ActualChat.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class PreviewLanguageCatalogTest
{
    [Fact]
    public void PreviewLanguageShouldRenderItsOwnCatalog()
    {
        // A newer device may have chosen Kannada after its release, and this build ships its catalog

        // act
        var label = StringCatalog.Get(Languages.Kannada, "Settings_Language");

        // assert
        Languages.Kannada.Support.Should().Be(LanguageSupport.Preview);
        label.Value.Should().Be("ಭಾಷೆ");
    }
}
