using ActualChat.Testing;
using ActualChat.Video;

namespace ActualChat.Streaming.UnitTests;

public class SerializationCodeGenTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        SerializationCodeGen.ValidateType<Change<string>>();
        SerializationCodeGen.ValidateType<AudioRecord>();
        SerializationCodeGen.ValidateType<VideoRecord>();
        SerializationCodeGen.ValidateType<VideoStreamHeader>();
        SerializationCodeGen.ValidateType<VideoStreamInfo>();
        SerializationCodeGen.ValidateType<VideoFormat>();
        SerializationCodeGen.ValidateType<VideoQualityPreset>();
    }
}
