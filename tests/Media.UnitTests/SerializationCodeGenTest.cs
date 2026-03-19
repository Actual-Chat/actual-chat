using ActualChat.Testing;

namespace ActualChat.Media.UnitTests;

public class SerializationCodeGenTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        SerializationCodeGen.ValidateType<Change<string>>();
        SerializationCodeGen.ValidateType<LinkPreviewsBackend_Change>();
        SerializationCodeGen.ValidateType<MediaProgress>();
        SerializationCodeGen.ValidateType<Media_ReserveMedia>();
        SerializationCodeGen.ValidateType<Media_RemoveMedia>();
        SerializationCodeGen.ValidateType<Media_UpdateProgress>();
        SerializationCodeGen.ValidateType<Media_ProcessUpload>();
    }
}
