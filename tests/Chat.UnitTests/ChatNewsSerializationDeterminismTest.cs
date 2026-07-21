using System.Security.Cryptography;

namespace ActualChat.Chat.UnitTests;

// Guards the RemoteComputedCache hash-match optimization for IChats.GetNews:
// the server hashes the serialized result, so identical values must serialize
// to identical bytes across calls and processes.
public sealed class ChatNewsSerializationDeterminismTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ChatNewsSerializationIsDeterministic()
    {
        // arrange
        var news = ChatNewsTestData.CreateChatNews();
        var s = MessagePackByteSerializer.Default;

        // act
        using var buffer1 = s.Write(news, typeof(ChatNews));
        var bytes1 = buffer1.WrittenSpan.ToArray();
        using var buffer2 = s.Write(news, typeof(ChatNews));
        var bytes2 = buffer2.WrittenSpan.ToArray();
        var copy = (ChatNews)s.Read(bytes1, typeof(ChatNews), out _)!;
        using var buffer3 = s.Write(copy, typeof(ChatNews));
        var bytes3 = buffer3.WrittenSpan.ToArray();
        var news2 = ChatNewsTestData.CreateChatNews();
        using var buffer4 = s.Write(news2, typeof(ChatNews));
        var bytes4 = buffer4.WrittenSpan.ToArray();

        // assert
        bytes2.Should().Equal(bytes1, "same instance must serialize to identical bytes");
        bytes3.Should().Equal(bytes1, "deserialize -> reserialize must produce identical bytes");
        bytes4.Should().Equal(bytes1, "identically-constructed instance must serialize to identical bytes");

        // Stable across processes? Run this test twice (separate runs) and compare the hash below.
        var hash = Convert.ToBase64String(SHA256.HashData(bytes1));
        Out.WriteLine($"ChatNews SHA256: {hash}");
        Out.WriteLine($"Length: {bytes1.Length}");
    }
}
