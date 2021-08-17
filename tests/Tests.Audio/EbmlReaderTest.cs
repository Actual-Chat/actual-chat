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
    public class EbmlReaderTest : TestBase
    {
        public EbmlReaderTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public async Task BasicReaderTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            using var bufferLease = MemoryPool<byte>.Shared.Rent(3 * 1024);
            var buffer = bufferLease.Memory;
            var bytesRead =await inputStream.ReadAsync(buffer);
            
            bytesRead.Should().BeGreaterThan(3 * 1024);

            var entries = Parse(buffer.Span).ToList();
            entries.Should().HaveCount(3);
            entries.Should().NotContainNulls();
            entries[0].Should().BeOfType<EBML>();
            entries[1].Should().BeOfType<Segment>();
            entries[2].Should().BeOfType<Cluster>();
            entries[2].As<Cluster>().SimpleBlocks.Should().HaveCount(10);
            entries[2].As<Cluster>().SimpleBlocks.Should().NotContainNulls();
        }
        
        [Fact]
        public async Task SequentalBlockReaderTest()
        {
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            using var bufferLease1 = MemoryPool<byte>.Shared.Rent(3 * 1024);
            using var bufferLease2 = MemoryPool<byte>.Shared.Rent(3 * 1024);
            var buffer1 = bufferLease1.Memory;
            var buffer2 = bufferLease2.Memory;
            var bytesRead1 = await inputStream.ReadAsync(buffer1);
            var bytesRead2 = await inputStream.ReadAsync(buffer2);
            
            bytesRead1.Should().BeGreaterThan(3 * 1024);
            bytesRead2.Should().BeGreaterThan(3 * 1024);

            var entries = Parse(buffer1.Span, buffer2.Span).ToList();
            entries.Should().HaveCount(3);
            entries.Should().NotContainNulls();
            entries[0].Should().BeOfType<EBML>();
            entries[1].Should().BeOfType<Segment>();
            entries[2].Should().BeOfType<Cluster>();
            entries[2].As<Cluster>().SimpleBlocks.Should().HaveCount(21);
            entries[2].As<Cluster>().SimpleBlocks.Should().NotContainNulls();
        }

        private List<BaseModel> Parse(Span<byte> span)
        {
            var result = new List<BaseModel>();
            var reader = new EbmlReader(span);
            while (reader.Read()) 
                result.Add(reader.Entry);
            result.Add(reader.Entry);
            return result;
        }
        
        private List<BaseModel> Parse(Span<byte> span1, Span<byte> span2)
        {
            var result = new List<BaseModel>();
            var reader = new EbmlReader(span1);
            while (reader.Read()) 
                result.Add(reader.Entry);
            
            // using var bufferLease1 = MemoryPool<byte>.Shared.Rent(3 * 1024);
            if (!reader.Tail.IsEmpty) {
                using var bufferLease = MemoryPool<byte>.Shared.Rent(span2.Length + reader.Tail.Length);
                var span = bufferLease.Memory.Span;
                reader.Tail.CopyTo(span);
                span2.CopyTo(span[reader.Tail.Length..]);
                reader = reader.WithNewSource(span);
            }
            else
                reader = reader.WithNewSource(span2);
            while (reader.Read()) 
                result.Add(reader.Entry);
            result.Add(reader.Entry);
            return result;
        }
    }
}