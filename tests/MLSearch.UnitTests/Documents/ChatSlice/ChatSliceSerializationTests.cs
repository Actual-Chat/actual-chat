using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.UnitTests.Documents.ChatSlice;

public class ChatSliceSerializationTests(ITestOutputHelper @out) : TestBase(@out)
{
    private const string AttachmentSummary = "Don't expect any media here.";

    private static readonly JsonSerializerOptions _serializerOptions = new() {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void AttachmentSerializesProperly()
    {
        var id = new MediaId("testscope", Generate.Option);
        var attachment = new ChatSliceAttachment(id, AttachmentSummary);

        var jsonString = JsonSerializer.Serialize(attachment, _serializerOptions);
        Assert.True(jsonString.Contains("id", StringComparison.Ordinal));
        Assert.True(jsonString.Contains("summary", StringComparison.Ordinal));
        Assert.True(jsonString.Contains("testscope:", StringComparison.Ordinal));
        Assert.True(jsonString.Contains("expect any media here.", StringComparison.Ordinal));
    }

    [Fact]
    public void AttachmentDeserializesProperly()
    {
        var id = new MediaId("testscope", Generate.Option);
        var attachment = new ChatSliceAttachment(id, AttachmentSummary);

        var jsonString = JsonSerializer.Serialize(attachment, _serializerOptions);

        var deserializedAttachment = JsonSerializer.Deserialize<ChatSliceAttachment>(jsonString, _serializerOptions);
        Assert.Equal(attachment, deserializedAttachment);
    }

    [Fact]
    public void ChatSliceMetadataSerializesAndDeserializesProperly()
    {
        var metadata = CreateMetadata();
        var jsonString = JsonSerializer.Serialize(metadata, _serializerOptions);
        var deserializedMetadata = JsonSerializer.Deserialize<ChatSliceMetadata>(jsonString, _serializerOptions);
        Assert.Equivalent(metadata, deserializedMetadata);
        Assert.Equal(jsonString, JsonSerializer.Serialize(deserializedMetadata, _serializerOptions));
    }

    [Fact]
    public void ChatSliceSerializesAndDeserializesProperly()
    {
        var metadata = CreateMetadata();
        var document = new MLSearch.Documents.ChatSlice(metadata, "Unique and valuable message");
        var jsonString = JsonSerializer.Serialize(document, _serializerOptions);
        var deserializedDocument = JsonSerializer.Deserialize<MLSearch.Documents.ChatSlice>(jsonString, _serializerOptions);
        Assert.Equivalent(document, deserializedDocument);
        Assert.Equal(jsonString, JsonSerializer.Serialize(deserializedDocument, _serializerOptions));
    }

    private static ChatSliceMetadata CreateMetadata()
    {
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var chatId = new ChatId(Generate.Option);
        var chatEntryId1 = new ChatEntryId(chatId, ChatEntryKind.Text, 1, AssumeValid.Option);
        var chatEntryId2 = new ChatEntryId(chatId, ChatEntryKind.Text, 2, AssumeValid.Option);
        var chatEntries = ImmutableArray.Create<ChatSliceEntry>(new (chatEntryId1, 1, 1), new (chatEntryId2, 2, 1));
        var (startOffset, endOffset) = (0, 100);
        var replyToEntries = ImmutableArray.Create(new ChatEntryId(chatId, ChatEntryKind.Text, 100, AssumeValid.Option));
        var activeUser = new PrincipalId(UserId.New(), AssumeValid.Option);
        var mentions = ImmutableArray.Create(activeUser);
        var reactions = ImmutableArray.Create(activeUser);
        var attachments = ImmutableArray.Create(
            new ChatSliceAttachment(new MediaId("chat", Generate.Option), "summary1"),
            new ChatSliceAttachment(new MediaId("chat", Generate.Option), "summary2")
        );
        const string lang = "en-US";
        var timestamp = DateTime.Now;

        return new ChatSliceMetadata(
            [authorId], chatEntries, startOffset, endOffset,
            replyToEntries, mentions, reactions, attachments,
            true, lang, timestamp
        );
    }
}
