using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class AttachmentListTest
{
    [Fact]
    public void NewListShouldStartUncommitted()
    {
        // act
        var list = new AttachmentList();

        // assert
        list.IsCommitted.Should().BeFalse();
    }

    [Fact]
    public void CommitShouldBeIdempotent()
    {
        // arrange
        var list = new AttachmentList();

        // act
        list.Commit();
        list.Commit();

        // assert
        list.IsCommitted.Should().BeTrue();
    }
}
