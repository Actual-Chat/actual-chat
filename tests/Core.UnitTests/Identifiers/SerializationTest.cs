using ActualLab.IO;

namespace ActualChat.Core.UnitTests.Identifiers;

public class SerializationTest(ITestOutputHelper @out)
{
    private static readonly TypeDecoratingByteSerializer Serializer = new(MemoryPackByteSerializer.Default);

    [Fact]
    public void SerializeChatId()
    {
        ChatId chatId = GroupChatId.Parse("chat12345");
        var buffer = new ArrayPoolBuffer<byte>();
        Serializer.Write(buffer, chatId, typeof(ChatId));
    }

    [Fact]
    public void SerializeEmoji()
    {
        var emoji = Emojis.ThumbsUp;
        var buffer = new ArrayPoolBuffer<byte>();
        var type = typeof(Emoji);
        Serializer.Write(buffer, emoji, type);
    }
}
