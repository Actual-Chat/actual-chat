using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class SerializationCodeGenTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        SerializationCodeGen.ValidateType<Change<string>>();
        SerializationCodeGen.ValidateMemoryPackType<ChatListSettings>();
        SerializationCodeGen.ValidateType<ActiveChat>();
        SerializationCodeGen.ValidateType<FileMetadata>();
        SerializationCodeGen.ValidateType<UploadSessionSnapshot>();
        SerializationCodeGen.ValidateType<RelatedEntryRef>();
    }
}
