using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ActualChat.Audio.WebM;
using ActualChat.Audio.WebM.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests
{
    public class WebMWriterTest : TestBase
    {
        public WebMWriterTest(ITestOutputHelper @out) : base(@out)
        {
        }
        
        [Fact]
        public async Task BasicWriterTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            using var bufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
            var buffer = bufferLease.Memory;
            var bytesRead =await inputStream.ReadAsync(buffer);

            var (entry1, state1) = Parse(new WebMReader(buffer.Span));
            var (entry2, state2) = Parse(WebMReader.FromState(state1).WithNewSource(buffer.Span[state1.Position..]));
            var (entry3, _) = Parse(WebMReader.FromState(state2).WithNewSource(buffer.Span[(state2.Position + state1.Position)..]));

            using var writeBufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
            var writeBuffer = writeBufferLease.Memory;
            var (ebmlWritten, position1) = Write(new WebMWriter(writeBuffer.Span), entry1);
            ebmlWritten.Should().BeTrue();
            
            var (segmentWritten, position2) = Write(new WebMWriter(writeBuffer.Span[position1..]), entry2);
            segmentWritten.Should().BeTrue();
            
            var (clusterWritten, position3) = Write(new WebMWriter(writeBuffer.Span[(position1+position2)..]), entry3);
            clusterWritten.Should().BeTrue();


            (RootEntry, WebMReader.State) Parse(WebMReader reader)
            {
                reader.Read();
                return (reader.Entry, reader.GetState());
            }

            (bool success, int position) Write(WebMWriter writer, RootEntry entry)
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
            var writeBuffer = writeBufferLease.Memory;
            WebMReader.State currentState = default;
            var bytesRead = await inputStream.ReadAsync(readBuffer[currentState.Position..]);
            while(bytesRead > 0) {
                var (elements, state) 
                    = Parse(currentState.IsEmpty 
                        ? new WebMReader(readBuffer.Span[..bytesRead]) 
                        : WebMReader.FromState(currentState).WithNewSource(readBuffer.Span[..(currentState.Remaining + bytesRead)]));
                currentState = state;

                var endPosition = Write(new WebMWriter(writeBuffer.Span), elements);
                
                AssertBuffersAreSame(readBuffer.Span, writeBuffer.Span, endPosition);

                readBuffer.Slice(currentState.Position, currentState.Remaining).CopyTo(readBuffer[..currentState.Remaining]);
                
                bytesRead = await inputStream.ReadAsync(readBuffer[currentState.Remaining..]);
            }
            
            (IReadOnlyList<RootEntry>, WebMReader.State) Parse(WebMReader reader)
            {
                var result = new List<RootEntry>();
                while (reader.Read()) 
                    result.Add(reader.Entry);
                return (result, reader.GetState());
            }

            int Write(WebMWriter writer, IReadOnlyList<RootEntry> elements)
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
        public async Task ReadWriteToFileTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            await using var outputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file-out.webm"), FileMode.OpenOrCreate, FileAccess.ReadWrite);
            using var readBufferLease = MemoryPool<byte>.Shared.Rent(10 * 1024);
            using var writeBufferLease = MemoryPool<byte>.Shared.Rent(100 * 1024);
            var readBuffer = readBufferLease.Memory;
            var writeBuffer = writeBufferLease.Memory;
            WebMReader.State currentState = default;
            var bytesRead = await inputStream.ReadAsync(readBuffer[currentState.Position..]);
            int endPosition = 0;
            while(bytesRead > 0) {
                var (elements, state) 
                    = Parse(currentState.IsEmpty 
                        ? new WebMReader(readBuffer.Span[..bytesRead]) 
                        : WebMReader.FromState(currentState).WithNewSource(readBuffer.Span[..(currentState.Remaining + bytesRead)]));
                currentState = state;

                endPosition = Write(new WebMWriter(writeBuffer.Span), elements);

                await outputStream.WriteAsync(writeBuffer[..endPosition]);
                
                readBuffer.Slice(currentState.Position, currentState.Remaining).CopyTo(readBuffer[..currentState.Remaining]);
                
                bytesRead = await inputStream.ReadAsync(readBuffer[currentState.Remaining..]);
            }
            endPosition = Write(new WebMWriter(writeBuffer.Span), (IReadOnlyList<RootEntry>)new[]{ (RootEntry)currentState.Container });
            await outputStream.WriteAsync(writeBuffer[..endPosition]);
            
            outputStream.Flush();
            
            (IReadOnlyList<RootEntry>, WebMReader.State) Parse(WebMReader reader)
            {
                var result = new List<RootEntry>();
                while (reader.Read()) 
                    result.Add(reader.Entry);
                return (result, reader.GetState());
            }

            int Write(WebMWriter writer, IReadOnlyList<RootEntry> elements)
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