namespace ActualChat.Core.UnitTests.Identifiers;

public class ContentRefTest(ITestOutputHelper @out) : StringIdentifierTestBase<ContentRef>(@out)
{
    public override string[] ValidIdentifiers
        => ["u:abcdef", "c:abcdef", "ce:abcdef:0:1", "a:abcdef:1", "p:abcdefghij"];

    public override string[] InvalidIdentifiers
        => ["", "abcdef", ":abcdef", "u:", "u:!", "unknown:abcdef"];

    [Fact]
    public void ContentRefShouldRememberItsContentIdAndBeCached()
    {
        // arrange
        var userId = UserId.Parse("abcdef");

        // act
        var contentRef = userId.ContentRef;

        // assert
        contentRef.Value.Should().Be("u:abcdef");
        contentRef.Id.Value.Should().Be(contentRef.Value);
        contentRef.HashCode.Should().Be(contentRef.Value.GetHashCode());
        contentRef.ContentId.Should().BeSameAs(userId);
        userId.ContentRef.Should().BeSameAs(contentRef);
        ContentRef.Parse(contentRef.Value).Should().BeSameAs(contentRef);
        typeof(ContentId).IsAssignableFrom(typeof(ContentRef)).Should().BeFalse();
        contentRef.AssertPassesThroughSerializers(Out);
    }

    [Fact]
    public void DifferentContentTypesShouldRemainDistinct()
    {
        // arrange
        var userId = UserId.Parse("abcdef");
        var chatId = ChatId.Parse("abcdef");

        // act
        var ids = new HashSet<ContentRef> { userId.ContentRef, chatId.ContentRef };

        // assert
        ids.Should().HaveCount(2);
        chatId.ContentRef.Value.Should().Be("c:abcdef");
        ContentRef.Parse("c:abcdef").ContentId.Should().Be(chatId);
        (userId.ContentRef == new ContentRef(userId)).Should().BeTrue();
        (userId.ContentRef == chatId.ContentRef).Should().BeFalse();
    }

    [Fact]
    public void RegisteredPrefixesShouldRoundTrip()
    {
        // arrange
        (string Prefix, ContentId Id)[] cases = [
            ("ct", ContactId.NewAny(UserId.Parse("abcdef"), ChatId.Parse("ghijkl"))),
            ("cnv", ConversationId.New(ChatId.Parse("ghijkl"), 1)),
            ("loc", SharedLocationId.Parse("abcdefghij")),
        ];

        // act, assert
        foreach (var (prefix, id) in cases) {
            var contentRef = id.ContentRef;
            contentRef.Value.Should().Be($"{prefix}:{id.Value}");
            ContentRef.Parse(contentRef.Value).ContentId.Should().Be(id);
            contentRef.AssertPassesThroughSerializers(Out);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcdef")]
    [InlineData(":abcdef")]
    [InlineData("u:")]
    [InlineData("u:!")]
    [InlineData("unknown:abcdef")]
    [InlineData("language:en")]
    [InlineData("transcriber:")]
    [InlineData("e:smile")]
    [InlineData("~:my-alias")]
    [InlineData("external-contact:abcdef:device:1")]
    [InlineData("upload:abcdefghijklmnopqrst")]
    public void InvalidContentRefsShouldBeRejected(string? value)
    {
        // act
        var isParsed = ContentRef.TryParse(value, out var id);

        // assert
        isParsed.Should().BeFalse();
        id.Should().BeNull();
        FluentActions.Invoking(() => ContentRef.Parse(value)).Should().Throw<FormatException>();
    }
}
