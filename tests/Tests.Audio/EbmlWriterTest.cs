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
        public async Task BasicWriterTest()
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
            var (ebmlWritten, position1) = Write(new EbmlWriter(writeBuffer.Span), entry1);
            ebmlWritten.Should().BeTrue();
            
            var (segmentWritten, position2) = Write(new EbmlWriter(writeBuffer.Span[position1..]), entry2);
            segmentWritten.Should().BeTrue();
            
            var (clusterWritten, position3) = Write(new EbmlWriter(writeBuffer.Span[(position1+position2)..]), entry3);
            clusterWritten.Should().BeTrue();


            (RootEntry, EbmlReader.State) Parse(EbmlReader reader)
            {
                reader.Read();
                return (reader.Entry, reader.GetState());
            }

            (bool success, int position) Write(EbmlWriter writer, RootEntry entry)
            {
                var success = writer.Write(entry);
                return (success, writer.Position);
            }
        }

        [Fact]
        public void CastTest()
        {
            long value = -300;
            ulong uvalue = (ulong)Math.Abs(value) | (1UL << (8*3 - 1));
            
            Out.WriteLine(uvalue.ToString("X"));
        }
        

    }
}