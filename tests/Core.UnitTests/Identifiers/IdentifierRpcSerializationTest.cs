using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Serialization;

namespace ActualChat.Core.UnitTests.Identifiers;

/// <summary>
/// Guards the RPC wire format of string-like identifiers: they must travel as a bare
/// string, never as a <see cref="TypeRef"/>-decorated value.
/// </summary>
public class IdentifierRpcSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly RpcArgumentSerializer ArgumentSerializer
        = new RpcByteArgumentSerializerV4(Serializers.MessagePack);

    public static TheoryData<string, Type> ChatIdCases => new() {
        { "1234abcd", typeof(GroupChatId) },
        { "p-admin1-admin2", typeof(PeerChatId) },
        { "s-abcdefghij-1234abcd", typeof(PlaceChatId) },
        { "1234abcd-7", typeof(ThreadChatId) },
        { "p-admin1-admin2-7", typeof(ThreadChatId) },
        { "s-abcdefghij-1234abcd-7", typeof(ThreadChatId) },
    };

    [Fact]
    public void StringLikeIdentifiersShouldNotBePolymorphicForRpc()
    {
        // arrange
        // Only self-parsing types: IStringLike<TSelf> closed over TSelf is what makes the
        // StringLikeXxx formatters able to rebuild the value from its string form.
        var types = new[] { typeof(StringIdentifier).Assembly, typeof(ChatId).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsInterface && !t.IsGenericTypeDefinition && IsSelfParsing(t))
            .ToArray();

        // act
        var polymorphic = types
            .Where(t => RpcArgumentSerializer.IsPolymorphic(t) || RpcArgumentSerializer.IsPolymorphic(t.MakeArrayType()))
            .Select(t => t.GetName())
            .ToArray();
        WriteLine($"{types.Length} string-like types, {polymorphic.Length} polymorphic");

        // assert
        types.Should().Contain([typeof(ChatId), typeof(PrincipalId)]);
        polymorphic.Should().BeEmpty(
            "string-like identifiers serialize as their string Value and rebuild via Parse, "
            + "so an abstract one needs [RpcSerializable] to stay off the polymorphic path");
    }

    [Theory]
    [MemberData(nameof(ChatIdCases))]
    public void ChatIdArgumentsShouldNotBeTypeDecorated(string value, Type expectedType)
    {
        // arrange
        var chatId = ChatId.Parse(value);
        var arguments = ArgumentList.New(chatId);

        // act
        var data = Serialize(arguments, RpcArgumentSerializer.IsPolymorphic(typeof(ChatId)));
        var plain = Serialize(arguments, false);
        var copy = ArgumentList.New<ChatId>(null!);
        ArgumentSerializer.Deserialize(ref copy, false, data);
        WriteLine($"{expectedType.GetName()} '{value}' -> {data.AsByteString()}");

        // assert
        data.Should().Equal(plain, "the polymorphism flag RPC derives for ChatId must not alter its encoding");
        copy.Get<ChatId>(0).Should().Be(chatId).And.BeOfType(expectedType);
    }

    [Theory]
    [MemberData(nameof(ChatIdCases))]
    public void SerializersShouldPreserveChatIdSubtype(string value, Type expectedType)
    {
        // arrange
        // The declared type picks the serializer, so it must stay ChatId rather than the subtype
        ChatId chatId = ChatId.Parse(value);
        chatId.Should().BeOfType(expectedType);

        // act
        var copy = chatId.AssertPassesThroughSerializers(Out);

        // assert
        copy.Should().BeOfType(expectedType);
    }

    // Private methods

    private static bool IsSelfParsing(Type type)
        => type.GetInterfaces().Any(i => i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IStringLike<>)
            && i.GetGenericArguments()[0] == type);

    private static byte[] Serialize(ArgumentList arguments, bool needsPolymorphism)
    {
        using var buffer = new ArrayPoolBuffer<byte>();
        ArgumentSerializer.Serialize(arguments, needsPolymorphism, buffer);
        return buffer.WrittenMemory.ToArray();
    }
}
