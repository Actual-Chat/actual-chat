using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests
{
    public class WebMParseTest : TestBase
    {
        public WebMParseTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public async Task BasicParseTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            // var matroskaDocument = MatroskaSerializer.Deserialize(inputStream);
            
            // Out.WriteLine(matroskaDocument.Ebml.DocType);
            // await using var outputStream = File.OpenWrite(Path.Combine(Environment.CurrentDirectory, "data", "file.opus"));
            // MatroskaDemuxer.ExtractOggOpusAudio(inputStream, outputStream, new OggOpusAudioStreamDemuxerSettings{MaxSegmentPartsPerOggPage = 10});
            
            // MemoryPool<byte>.Shared.Rent()
        }
    }
}