using ActualChat.Chat;

namespace ActualChat.Chat.UnitTests;

public class TranslationDubTest
{
    private static readonly ChatId ChatId = ChatId.Parse("p-Nb5srp-pKGsAk");
    private static readonly TranslationId Id =
        TranslationId.New(ChatEntryId.New(ChatId, 590), Languages.English);

    [Fact]
    public void DubShouldBeValidWhenMadeFromCurrentContent()
    {
        // arrange
        var translation = new Translation(Id) {
            Content = "Hello",
            SourceContentHash = ChatEntryHashExt.GetContentHashString("Привет"),
            DubMediaId = MediaId.New(ChatId.Value),
            DubContentHash = ChatEntryHashExt.GetContentHashString("Hello"),
        };

        // act & assert
        translation.HasValidDub().Should().BeTrue();
    }

    [Fact]
    public void DubShouldBeInvalidWhenMadeFromOlderContent()
    {
        // arrange
        var translation = new Translation(Id) {
            Content = "Hello there",
            SourceContentHash = ChatEntryHashExt.GetContentHashString("Привет"),
            DubMediaId = MediaId.New(ChatId.Value),
            DubContentHash = ChatEntryHashExt.GetContentHashString("Hello"),
        };

        // act & assert
        translation.HasValidDub().Should().BeFalse();
    }

    [Fact]
    public void DubShouldBeValidOnlyForTheVoiceItWasMadeWith()
    {
        // arrange
        var translation = new Translation(Id) {
            Content = "Hello",
            SourceContentHash = ChatEntryHashExt.GetContentHashString("Привет"),
            DubMediaId = MediaId.New(ChatId.Value),
            DubContentHash = TranslationDubExt.GetDubContentHash("Hello", "Daniel"),
        };

        // act & assert
        translation.HasValidDub("Daniel").Should().BeTrue();
        translation.HasValidDub("Nina").Should().BeFalse("a voice change must regenerate the dub");
        translation.HasValidDub().Should().BeFalse("the default voice is not the one it was made with");
    }

    [Fact]
    public void DefaultVoiceHashShouldMatchTheContentHash()
    {
        // act & assert
        // Dubs stored before voices existed carry the plain content hash and must stay valid
        TranslationDubExt.GetDubContentHash("Hello").Should().Be(ChatEntryHashExt.GetContentHashString("Hello"));
        TranslationDubExt.GetDubContentHash("Hello", "Daniel").Should()
            .NotBe(ChatEntryHashExt.GetContentHashString("Hello"));
    }

    [Fact]
    public void DubShouldBeInvalidWhenMediaIsMissing()
    {
        // arrange
        var translation = new Translation(Id) { Content = "Hello" };

        // act & assert
        translation.HasValidDub().Should().BeFalse();
    }
}
