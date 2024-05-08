
using ActualChat.Chat;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentCursorTests(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly JsonSerializerOptions _serializerOptions = new() {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void ConstructionFromChatEntryWorksProperly()
    {
        const long version = 1001;
        const long localId = 20202;
        var chatId = new ChatId(Generate.Option);
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option);
        var chatEntry = new ChatEntry {
            Id = chatEntryId,
            Version = version
        };

        var cursor = new ChatContentCursor(chatEntry);
        Assert.Equal(localId, cursor.LastEntryLocalId);
        Assert.Equal(version, cursor.LastEntryVersion);
    }

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

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
    [Fact]
    public void ComparisonWithNullWorksAsExpected()
    {
        const long lastVersion = 1001;
        const long lastLocalId = 20202;
        var cursor = new ChatContentCursor(lastVersion, lastLocalId);
        Assert.True(cursor > null);
        Assert.False(cursor <= null);
        Assert.True(null < cursor);
        Assert.False(null >= cursor);
        Assert.True(default(ChatContentCursor) <= null);
        Assert.True(default(ChatContentCursor) >= null);
        Assert.False(default(ChatContentCursor) > null);
        Assert.False(default(ChatContentCursor) < null);
        Assert.True(cursor.CompareTo(null) > 0);
    }
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

    [Theory]
    [MemberData(nameof(LocalIdPairs))]
    public void CursorWithGreaterVersionIsAlwaysGreater(long id1, long id2)
    {
        var cursor1 = new ChatContentCursor(10001, id1);
        var cursor2 = new ChatContentCursor(10000, id2);
        Assert.True(cursor1 > cursor2);
        Assert.True(cursor1 >= cursor2);
        Assert.True(cursor2 < cursor1);
        Assert.True(cursor2 <= cursor1);
        Assert.True(cursor1.CompareTo(cursor2) > 0);
        Assert.True(cursor2.CompareTo(cursor1) < 0);
    }

    [Theory]
    [MemberData(nameof(LocalIdPairs))]
    public void CursorComparisonDependsOnIdWhenVersionsEqual(long id1, long id2)
    {
        var cursor1 = new ChatContentCursor(10000, id1);
        var cursor2 = new ChatContentCursor(10000, id2);
        Assert.Equal(id1 > id2, cursor1 > cursor2);
        Assert.Equal(id1 >= id2, cursor1 >= cursor2);
        Assert.Equal(id1 < id2, cursor1 < cursor2);
        Assert.Equal(id1 <= id2, cursor1 <= cursor2);
        Assert.Equal(id1.CompareTo(id2), cursor1.CompareTo(cursor2));
    }

    public static TheoryData<long, long> LocalIdPairs => new() {
        {1000, 999}, {1000, 1000}, {1000, 1001}
    };
}
