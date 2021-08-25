using ActualChat.Mathematics;
using FluentAssertions;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests
{
    public class WordPrefixTreeTest : TestBase
    {
        public WordPrefixTreeTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public void BuildTrieTest()
        {
            var trie = new WordPrefixTree<double>();
            
            trie.Add("A".Split(), 12);
            trie.Add("A".Split(), 13);
            trie.Add("A B".Split(), 20);
            trie.Add("B B".Split(), 21);
            trie.Add("B B CC D DD".Split(), 25);
            trie.Add("XY Z".Split(), 21);
            trie.Add("B B CC D DE F".Split(), 26);
            trie.Add("B B CC D DD EX".Split(), 27);
            trie.Add("B B CC D DD EXA".Split(), 29);
            trie.Add("B B CC D DD EXA A".Split(), 33);
            trie.Add("B B CC D DD EXA AB".Split(), 40);

            var (prefix, data) = trie.GetCommonPrefix("B B CC".Split());
            prefix.Count.Should().Be(3);
            data.Count.Should().Be(1);
            data[0].Should().Be(21);
        }
    }
}