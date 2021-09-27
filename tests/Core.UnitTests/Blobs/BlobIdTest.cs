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
            BlobPath.GetScope("a").Should().Be("");
            BlobPath.GetScope("a/b").Should().Be("a");
        }
    }
}
