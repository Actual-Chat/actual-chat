namespace ActualChat.Core.UnitTests.Identifiers;

public class TypedObjectIdTest(ITestOutputHelper @out) : StringIdentifierTestBase<TypedObjectId>(@out)
{
    public override string[] ValidIdentifiers
        => ["u:abcdef", "c:abcdef", "ce:abcdef:0:1", "a:abcdef:1", "p:abcdefghij"];

    public override string[] InvalidIdentifiers
        => ["", "abcdef", ":abcdef", "u:", "u:!", "unknown:abcdef"];

    [Fact]
    public void TypedIdShouldRememberItsObjectIdAndBeCached()
    {
        // arrange
        var userId = UserId.Parse("abcdef");

        // act
        var typedId = userId.TypedId;

        // assert
        typedId.Value.Should().Be("u:abcdef");
        typedId.Id.Value.Should().Be(typedId.Value);
        typedId.HashCode.Should().Be(typedId.Value.GetHashCode());
        typedId.ObjectId.Should().BeSameAs(userId);
        userId.TypedId.Should().BeSameAs(typedId);
        TypedObjectId.Parse(typedId.Value).Should().BeSameAs(typedId);
        typeof(ObjectId).IsAssignableFrom(typeof(TypedObjectId)).Should().BeFalse();
        typedId.AssertPassesThroughSerializers(Out);
    }

    [Fact]
    public void DifferentObjectTypesShouldRemainDistinct()
    {
        // arrange
        var userId = UserId.Parse("abcdef");
        var chatId = ChatId.Parse("abcdef");

        // act
        var ids = new HashSet<TypedObjectId> { userId.TypedId, chatId.TypedId };

        // assert
        ids.Should().HaveCount(2);
        chatId.TypedId.Value.Should().Be("c:abcdef");
        TypedObjectId.Parse("c:abcdef").ObjectId.Should().Be(chatId);
        (userId.TypedId == new TypedObjectId(userId)).Should().BeTrue();
        (userId.TypedId == chatId.TypedId).Should().BeFalse();
    }

    [Fact]
    public void ReviewedPrefixesShouldRoundTrip()
    {
        // arrange
        (string Prefix, ObjectId Id)[] cases = [
            ("~", AliasId.Parse("my-alias")),
            ("ct", ContactId.NewAny(UserId.Parse("abcdef"), ChatId.Parse("ghijkl"))),
            ("conv", ConversationId.New(ChatId.Parse("ghijkl"), 1)),
        ];

        // act, assert
        foreach (var (prefix, id) in cases) {
            var typedId = id.TypedId;
            typedId.Value.Should().Be($"{prefix}:{id.Value}");
            TypedObjectId.Parse(typedId.Value).ObjectId.Should().Be(id);
            typedId.AssertPassesThroughSerializers(Out);
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
    public void InvalidTypedIdsShouldBeRejected(string? value)
    {
        // act
        var success = TypedObjectId.TryParse(value, out var id);

        // assert
        success.Should().BeFalse();
        id.Should().BeNull();
        FluentActions.Invoking(() => TypedObjectId.Parse(value)).Should().Throw<FormatException>();
    }
}
