using ActualChat.Serialization.Internal;

namespace ActualChat.Core.UnitTests.Identifiers;

public class TypedObjectIdSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("u:abcdef")]
    [InlineData("c:abcdef")]
    [InlineData("ce:abcdef:0:1")]
    [InlineData("a:abcdef:1")]
    [InlineData("p:abcdefghij")]
    public void TypedIdsShouldSerializeAsTheirValue(string value)
    {
        // arrange
        var id = TypedObjectId.Parse(value);
        var options = MessagePackByteSerializer.DefaultOptions;
        var expected = MessagePackSerializer.Serialize(value, options);

        // act
        var bytes = MessagePackSerializer.Serialize(id, options);

        // assert
        bytes.Should().Equal(expected);
        MessagePackSerializer.Deserialize<TypedObjectId>(expected, options).Should().Be(id);
        MessagePackSerializer.Serialize(id, new MessagePackSerializerOptions(AppMessagePackKeylessResolver.Instance))
            .Should().Equal(expected);
        id.AssertPassesThroughSerializers(Out);
    }

    [Theory]
    [InlineData("u:abcdef")]
    [InlineData("c:abcdef")]
    public void JsonDictionaryKeysShouldUseTypedValues(string value)
    {
        // arrange
        var id = TypedObjectId.Parse(value);
        var items = new Dictionary<TypedObjectId, int> { [id] = 1 };
        var expected = "{\"" + value + "\":1}";

        // act
        var systemJson = new SystemJsonSerializer(JsonSerializerOptions.Default).Write(items);
        var newtonsoftJson = new NewtonsoftJsonSerializer().Write(items);

        // assert
        systemJson.Should().Be(expected);
        newtonsoftJson.Should().Be(expected);
        items.PassThroughSystemJsonSerializer().Keys.Should().ContainSingle().Which.Should().Be(id);
        items.PassThroughNewtonsoftJsonSerializer().Keys.Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public void ContentLinkInfoShouldSerializeWithItsTypedObjectId()
    {
        // arrange
        var id = ChatId.Parse("abcdef").TypedId;
        var info = new ContentLinkInfo(id, "Title", null, "Description");

        // act
        var copy = info.PassThroughSerializers(Out);

        // assert
        copy.Should().Be(info);
        copy.Id.ObjectId.Should().Be(id.ObjectId);
        copy.Id.Value.Should().Be("c:abcdef");
    }
}
