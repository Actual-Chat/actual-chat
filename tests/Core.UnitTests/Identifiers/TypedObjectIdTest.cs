namespace ActualChat.Core.UnitTests.Identifiers;

public class TypedObjectIdTest(ITestOutputHelper @out) : TestBase(@out)
{
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcdef")]
    [InlineData(":abcdef")]
    [InlineData("u:")]
    [InlineData("u:!")]
    [InlineData("unknown:abcdef")]
    public void InvalidTypedIdsShouldBeRejected(string? value)
    {
        // act
        var success = TypedObjectId.TryParse(value, out var id);

        // assert
        success.Should().BeFalse();
        id.Should().BeNull();
        FluentActions.Invoking(() => TypedObjectId.Parse(value)).Should().Throw<FormatException>();
    }

    [Fact]
    public void EmptyObjectIdsShouldRoundTripWhenTheirTypeAllowsThem()
    {
        // arrange
        var id = TranscriberId.None.TypedId;

        // act
        var parsed = TypedObjectId.Parse(id.Value);

        // assert
        id.Value.Should().Be("transcriber:");
        parsed.ObjectId.Should().BeSameAs(TranscriberId.None);
        id.AssertPassesThroughSerializers(Out);
    }
}
