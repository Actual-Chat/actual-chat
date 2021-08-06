using System;
using ActualChat.Blobs;
using FluentAssertions;
using Xunit;

namespace ActualChat.Tests.Blobs
{
    public class BlobIdTest
    {
        [Fact]
        public void GetScopeTest()
        {
            BlobId.GetScope("a").Should().Be("");
            BlobId.GetScope("a/b").Should().Be("a");
        }
    }
}
