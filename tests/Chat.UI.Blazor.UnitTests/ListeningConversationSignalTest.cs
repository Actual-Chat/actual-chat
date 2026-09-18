using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ListeningConversationSignalTest
{
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");
    private static readonly ChatId ChatB = ChatId.Parse("0NYND2MfRb");
    private static readonly AuthorId Me = AuthorId.New(ChatA, 1);
    private static readonly AuthorId Peer = AuthorId.New(ChatA, 2);

    [Fact]
    public void AnOwnStreamShouldNotBeAnInterjection()
    {
        // act - heard or not (ListenOwnAudio), our own stream must never cut our own utterance
        var isInterjection = ChatListeningPlayer.IsPeerInterjection(Me, Me, Recording(ChatA), ChatA);

        // assert
        isInterjection.Should().BeFalse();
    }

    [Fact]
    public void APeerStreamWhileRecordingHereShouldBeAnInterjection()
    {
        // act
        var isInterjection = ChatListeningPlayer.IsPeerInterjection(Me, Peer, Recording(ChatA), ChatA);

        // assert
        isInterjection.Should().BeTrue();
    }

    [Fact]
    public void APeerStreamWhileRecordingElsewhereShouldNotBeAnInterjection()
    {
        // act
        var isInterjection = ChatListeningPlayer.IsPeerInterjection(Me, Peer, Recording(ChatB), ChatA);

        // assert
        isInterjection.Should().BeFalse();
    }

    [Fact]
    public void APeerStreamWhileNotRecordingShouldNotBeAnInterjection()
    {
        // act
        var isInterjection = ChatListeningPlayer.IsPeerInterjection(Me, Peer, AudioRecorderState.Idle, ChatA);

        // assert
        isInterjection.Should().BeFalse();
    }

    [Fact]
    public void APeerStreamWithNoOwnAuthorShouldBeAnInterjection()
    {
        // act
        var isInterjection = ChatListeningPlayer.IsPeerInterjection(null, Peer, Recording(ChatA), ChatA);

        // assert
        isInterjection.Should().BeTrue();
    }

    private static AudioRecorderState Recording(ChatId chatId)
        => new(chatId) { IsRecording = true };
}
