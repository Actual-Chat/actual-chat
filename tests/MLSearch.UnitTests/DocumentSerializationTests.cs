namespace ActualChat.MLSearch.UnitTests;

public class DocumentSerializationTests(ITestOutputHelper @out) : TestBase(@out)
{
    private const string attachmentSummary = "Don't expect any media here.";

    private static readonly JsonSerializerOptions serializerOptions = new() {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void AttachmentSerializesProperly()
    {
        var id = new MediaId("testscope", Generate.Option);
        var attachment = new DocumentAttachment(id, attachmentSummary);

        var jsonString = JsonSerializer.Serialize(attachment, serializerOptions);
        Assert.True(jsonString.Contains("id", StringComparison.Ordinal));
        Assert.True(jsonString.Contains("summary", StringComparison.Ordinal));
        Assert.True(jsonString.Contains("testscope:", StringComparison.Ordinal));
        Assert.True(jsonString.Contains("expect any media here.", StringComparison.Ordinal));
    }

    [Fact]
    public void AttachmentDeserializesProperly()
    {
        var id = new MediaId("testscope", Generate.Option);
        var attachment = new DocumentAttachment(id, attachmentSummary);

        var jsonString = JsonSerializer.Serialize(attachment, serializerOptions);

        var deserializedAttachment = JsonSerializer.Deserialize<DocumentAttachment>(jsonString, serializerOptions);
        Assert.Equal(attachment.Id, deserializedAttachment.Id);
        Assert.Equal(attachment.Summary, deserializedAttachment.Summary);
    }
}
