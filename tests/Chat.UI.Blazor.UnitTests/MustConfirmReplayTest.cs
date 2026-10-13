using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class MustConfirmReplayTest
{
    private static readonly ChatId ChatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly ChatId ChatB = ChatId.Parse("bbbbbbbbbbbbbbbbbbbb");

    [Fact]
    public void ShouldNotConfirmWhenNoListeningChats()
    {
        // act
        var result = ChatAudioUI.MustConfirmReplay([], _ => true);

        // assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldNotConfirmWhenListeningChatsAreSilent()
    {
        // arrange
        ChatId[] listeningChatIds = [ChatA, ChatB];

        // act
        var result = ChatAudioUI.MustConfirmReplay(listeningChatIds, _ => false);

        // assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldConfirmWhenAnyListeningChatIsPlaying()
    {
        // arrange
        ChatId[] listeningChatIds = [ChatA, ChatB];

        // act
        var result = ChatAudioUI.MustConfirmReplay(listeningChatIds, chatId => chatId == ChatB);

        // assert
        result.Should().BeTrue();
    }
}
