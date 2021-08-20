using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ActualChat.Audio.Ebml;
using ActualChat.Audio.Ebml.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
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
        public async Task ReadWriteMatchTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            using var readBufferLease = MemoryPool<byte>.Shared.Rent(10 * 1024);
            using var writeBufferLease = MemoryPool<byte>.Shared.Rent(10 * 1024);
            var readBuffer = readBufferLease.Memory;
            var writeBuffer = readBufferLease.Memory;
            EbmlReader.State currentState = default;
            var bytesRead = await inputStream.ReadAsync(readBuffer[currentState.Position..]);
            while(bytesRead > 0) {
                var (elements, state) 
                    = Parse(currentState.IsEmpty 
                        ? new EbmlReader(readBuffer.Span) 
                        : EbmlReader.FromState(currentState).WithNewSource(readBuffer.Span[..bytesRead]));
                currentState = state;

                var endPosition = Write(new EbmlWriter(writeBuffer.Span), elements);
                
                AssertBuffersAreSame(readBuffer.Span, writeBuffer.Span, endPosition);

                readBuffer[currentState.Position..].CopyTo(readBuffer[..^currentState.Position]);
                
                bytesRead = await inputStream.ReadAsync(readBuffer[currentState.Remaining..]);
            }
            
            (IReadOnlyList<RootEntry>, EbmlReader.State) Parse(EbmlReader reader)
            {
                var result = new List<RootEntry>();
                while (reader.Read()) 
                    result.Add(reader.Entry);
                return (result, reader.GetState());
            }

            int Write(EbmlWriter writer, IReadOnlyList<RootEntry> elements)
            {
                foreach (var element in elements) {
                    var success = writer.Write(element);
                    success.Should().BeTrue();
                }
                return writer.Position;
            }

            void AssertBuffersAreSame(ReadOnlySpan<byte> read, ReadOnlySpan<byte> written, int endPosition)
            {
                var readSpan = read[..endPosition]; 
                var writtenSpan = written[..endPosition]; 
                for (int i = 0; i < endPosition; i++) {
                    readSpan[i].Should().Be(writtenSpan[i], "should match at Index {0}", i);
                }
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