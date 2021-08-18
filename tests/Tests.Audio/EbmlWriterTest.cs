using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ActualChat.Audio.Ebml;
using ActualChat.Audio.Ebml.Models;
using FluentAssertions;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests
{
    public class EbmlWriterTest : TestBase
    {
        public EbmlWriterTest(ITestOutputHelper @out) : base(@out)
        {
        }
        
        [Fact]
        public async Task BasicReaderTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            using var bufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
            var buffer = bufferLease.Memory;
            var bytesRead =await inputStream.ReadAsync(buffer);

            var (entry1, state1) = Parse(new EbmlReader(buffer.Span));
            var (entry2, state2) = Parse(EbmlReader.FromState(state1).WithNewSource(buffer.Span[state1.Position..]));
            var (entry3, _) = Parse(EbmlReader.FromState(state2).WithNewSource(buffer.Span[(state2.Position + state1.Position)..]));

            using var writeBufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
            var writeBuffer = writeBufferLease.Memory;
            var ebmlWritten = Write(new EbmlWriter(writeBuffer.Span), entry1);

            ebmlWritten.Should().BeTrue();

            (RootEntry, EbmlReader.State) Parse(EbmlReader reader)
            {
                reader.Read();
                return (reader.Entry, reader.GetState());
            }

            bool Write(EbmlWriter writer, RootEntry entry)
            {
                return writer.Write(entry);
            }
        }
        
    }
}