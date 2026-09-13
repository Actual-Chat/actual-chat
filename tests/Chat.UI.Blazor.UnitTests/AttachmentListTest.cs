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
    public void CommitShouldBeIdempotentAndRaiseOnce()
    {
        // arrange
        var list = new AttachmentList();
        var commits = 0;
        list.Committed += () => commits++;

        // act
        list.Commit();
        list.Commit();

        // assert
        list.IsCommitted.Should().BeTrue();
        commits.Should().Be(1);
    }
}
