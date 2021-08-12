using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;
using ActualChat.Audio.Ebml;
using FluentAssertions;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests
{
    public class EbmlReaderTest : TestBase
    {
        public EbmlReaderTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public async Task BasicReaderTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            using var bufferLease = MemoryPool<byte>.Shared.Rent(10 * 1024);
            var buffer = bufferLease.Memory;
            var bytesRead =await inputStream.ReadAsync(buffer);
            
            bytesRead.Should().BeGreaterThan(10 * 1024);

            Parse(buffer.Span);
            // var span = new bool[100 * 1024].AsSpan();
            // inputStream.Read(span);
        }

        private void Parse(Span<byte> span)
        {
            var reader = new EbmlReader(span);
            while (reader.Read()) {
                Out.WriteLine(reader.CurrentDescriptor.ToString());
                
            }
        }
    }
}