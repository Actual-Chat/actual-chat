
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentCursorTests(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly JsonSerializerOptions _serializerOptions = new() {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void ChatContentCursorSerializesAndDeserializesProperly()
    {
        const long lastVersion = 1001;
        const long lastLocalId = 20202;
        var cursor = new ChatContentCursor(lastVersion, lastLocalId);
        var jsonString = JsonSerializer.Serialize(cursor, _serializerOptions);
        var deserializedCursor = JsonSerializer.Deserialize<ChatContentCursor>(jsonString, _serializerOptions);
        Assert.Equivalent(cursor, deserializedCursor);
        Assert.Equal(jsonString, JsonSerializer.Serialize(deserializedCursor, _serializerOptions));
    }
}
