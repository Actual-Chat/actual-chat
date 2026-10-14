namespace ActualChat.Media.UnitTests;

public sealed class GithubLinkParserTest
{
    [Theory]
    [InlineData("https://github.com/Actual-Chat/actual-chat/pull/5168", GithubLinkKind.PullRequest, 5168, "")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/pull/5168/files", GithubLinkKind.PullRequest, 5168, "")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/pull/5168#discussion_r1", GithubLinkKind.PullRequest, 5168, "")]
    [InlineData("https://www.github.com/Actual-Chat/actual-chat/pull/5168?diff=split", GithubLinkKind.PullRequest, 5168, "")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/issues/4898", GithubLinkKind.Issue, 4898, "")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/issues/4898#issuecomment-1", GithubLinkKind.Issue, 4898, "")]
    [InlineData(
        "https://github.com/Actual-Chat/actual-chat/commit/7F7E3DE8A3",
        GithubLinkKind.Commit, 0, "7f7e3de8a3")]
    [InlineData(
        "https://github.com/Actual-Chat/actual-chat/commit/7f7e3de8a3b2c1d0e9f8a7b6c5d4e3f2a1b0c9d8?diff=unified",
        GithubLinkKind.Commit, 0, "7f7e3de8a3b2c1d0e9f8a7b6c5d4e3f2a1b0c9d8")]
    [InlineData("https://github.com/Actual-Chat/actual-chat", GithubLinkKind.Repository, 0, "")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/", GithubLinkKind.Repository, 0, "")]
    [InlineData("http://github.com/Actual-Chat/actual.chat-2", GithubLinkKind.Repository, 0, "")]
    public void GithubLinkShouldGiveItsTarget(string url, GithubLinkKind kind, int number, string sha)
    {
        // act
        var target = GithubLinkParser.TryParse(url);

        // assert
        target.Should().NotBeNull();
        target.Kind.Should().Be(kind);
        target.Owner.Should().Be("Actual-Chat");
        target.Name.Should().StartWith("actual");
        target.Number.Should().Be(number);
        target.Sha.Should().Be(sha);
    }

    [Theory]
    [InlineData("https://github.com")]
    [InlineData("https://github.com/Actual-Chat")]
    [InlineData("https://github.com/orgs/Actual-Chat/projects/1")]
    [InlineData("https://github.com/features/actions")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/blob/dev/README.md")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/releases/tag/v2.8")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/pull/")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/pull/abc")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/pull/0")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/commit/xyz")]
    [InlineData("https://github.com/Actual-Chat/actual-chat/commit/7f7e3d")]
    [InlineData("https://gist.github.com/someone/0123456789abcdef")]
    [InlineData("https://gitlab.com/Actual-Chat/actual-chat/-/merge_requests/1")]
    [InlineData("ftp://github.com/Actual-Chat/actual-chat")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherLinkShouldGiveNoTarget(string? url)
    {
        // act
        var target = GithubLinkParser.TryParse(url);

        // assert
        target.Should().BeNull();
    }
}
