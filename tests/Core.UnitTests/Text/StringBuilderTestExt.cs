using System.Text;

namespace ActualChat.Core.UnitTests.Text;

public class StringBuilderExtTest
{
    [Fact]
    public void GetSuffix_ShouldWorkCorrectly_WithIncrementalBuilding()
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var strings = new List<string>();
        var sb = new StringBuilder();

        // Test various suffix lengths
        var suffixLengths = new[] { 10, 50, 100, 500, 1000, 2000 };
        foreach (var suffixLength in suffixLengths) {
            // Reset for each suffix length test
            strings.Clear();
            sb.Clear();

            // Build up content incrementally
            for (int iteration = 0; iteration < 200; iteration++) {
                // Generate a random string chunk
                var chunkType = random.Next(4);
                string chunk = chunkType switch {
                    0 => new string((char)('a' + random.Next(26)), random.Next(10, 50)),
                    1 => $"Line{iteration}\n",
                    2 => new string(' ', random.Next(5, 20)),
                    _ => Guid.NewGuid().ToString()
                };

                strings.Add(chunk);
                sb.Append(chunk);

                // Test both with and without newline splitting
                TestSuffixExtraction(sb, strings, suffixLength, splitOnNewLine: true);
                TestSuffixExtraction(sb, strings, suffixLength, splitOnNewLine: false);
            }
        }
    }

    private static void TestSuffixExtraction(
        StringBuilder sb,
        List<string> strings,
        int suffixLength,
        bool splitOnNewLine)
    {
        // Get the result from our method
        var result = sb.GetSuffix(suffixLength, splitOnNewLine);

        // Build the expected result from the string list
        var fullString = string.Join("", strings);
        string expected;
        if (fullString.Length <= suffixLength)
            expected = fullString;
        else {
            // Get the suffix
            var suffix = fullString.Substring(fullString.Length - suffixLength);
            if (splitOnNewLine) {
                // Find the first newline and split there
                var newlineIndex = suffix.IndexOf('\n');
                if (newlineIndex >= 0)
                    expected = suffix.Substring(newlineIndex + 1);
                else
                    expected = suffix;
            }
            else
                expected = suffix;
        }

        // Verify the result
        result.Should().Be(expected,
            $"StringBuilder length: {sb.Length}, suffixLength: {suffixLength}, splitOnNewLine: {splitOnNewLine}");

        // Additional validations
        if (sb.Length > suffixLength) {
            result.Length.Should().BeLessThanOrEqualTo(suffixLength,
                "Result should never exceed suffix length");

            if (splitOnNewLine && result.Length < suffixLength) {
                // If we split on newline, a result shouldn't start with a newline
                result.Should().NotStartWith("\n",
                    "Result should not start with newline after splitting");
            }
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void GetSuffix_ShouldHandleNewlinesCorrectly_WithVariousPatterns(int suffixLength)
    {
        var patterns = new[] {
            // Pattern: (prefix, content with newlines, suffix)
            ("", "line1\nline2\nline3\n", ""),
            ("prefix", "\n\n\n", "suffix"),
            ("", "\n", new string('x', suffixLength)),
            (new string('a', 50), "\nmiddle\n", new string('b', suffixLength)),
            (new string('x', suffixLength + 100), "\n", new string('y', suffixLength - 50)),
        };

        foreach (var (prefix, middle, suffix) in patterns) {
            var sb = new StringBuilder();
            sb.Append(prefix);
            sb.Append(middle);
            sb.Append(suffix);
            var fullString = prefix + middle + suffix;

            // Test with splitOnNewLine = true
            var result = sb.GetSuffix(suffixLength, splitOnNewLine: true);
            var expected = GetExpectedSuffix(fullString, suffixLength, splitOnNewLine: true);
            result.Should().Be(expected,
                $"Pattern: prefix={prefix.Length}, middle='{middle}', suffix={suffix.Length}");

            // Test with splitOnNewLine = false
            result = sb.GetSuffix(suffixLength, splitOnNewLine: false);
            expected = GetExpectedSuffix(fullString, suffixLength, splitOnNewLine: false);
            result.Should().Be(expected);
        }
    }

    [Fact]
    public void GetSuffix_ShouldHandleEdgeCases()
    {
        var suffixLength = 100;

        // Empty StringBuilder
        var sb = new StringBuilder();
        sb.GetSuffix(suffixLength).Should().BeEmpty();

        // Single character
        sb.Clear();
        sb.Append('x');
        sb.GetSuffix(suffixLength).Should().Be("x");

        // Exactly suffixLength
        sb.Clear();
        sb.Append(new string('x', suffixLength));
        sb.GetSuffix(suffixLength).Should().Be(new string('x', suffixLength));

        // One more than suffixLength
        sb.Clear();
        sb.Append(new string('x', suffixLength + 1));
        sb.GetSuffix(suffixLength).Should().Be(new string('x', suffixLength));

        // Only newlines
        sb.Clear();
        sb.Append(new string('\n', suffixLength + 50));
        var result = sb.GetSuffix(suffixLength, splitOnNewLine: true);
        result.Length.Should().BeLessThanOrEqualTo(suffixLength);

        // Newline at the exact boundary
        sb.Clear();
        sb.Append(new string('x', 50));
        sb.Append('\n');
        sb.Append(new string('y', suffixLength));
        result = sb.GetSuffix(suffixLength, splitOnNewLine: true);
        result.Should().Be(new string('y', suffixLength));
    }

    [Fact]
    public void GetSuffix_ShouldBeConsistent_WithMultipleCalls()
    {
        var sb = new StringBuilder();
        var random = new Random(123);

        // Build a complex StringBuilder
        for (int i = 0; i < 100; i++) {
            sb.Append(new string((char)('a' + random.Next(26)), random.Next(20, 50)));
            if (random.Next(3) == 0)
                sb.Append('\n');
        }

        var suffixLength = 500;

        // Call multiple times - should get same result
        var result1 = sb.GetSuffix(suffixLength, splitOnNewLine: true);
        var result2 = sb.GetSuffix(suffixLength, splitOnNewLine: true);
        var result3 = sb.GetSuffix(suffixLength, splitOnNewLine: true);

        result1.Should().Be(result2);
        result2.Should().Be(result3);

        // Same for splitOnNewLine = false
        result1 = sb.GetSuffix(suffixLength, splitOnNewLine: false);
        result2 = sb.GetSuffix(suffixLength, splitOnNewLine: false);
        result3 = sb.GetSuffix(suffixLength, splitOnNewLine: false);

        result1.Should().Be(result2);
        result2.Should().Be(result3);
    }

    // Helper method to calculate expected result
    private static string GetExpectedSuffix(string fullString, int suffixLength, bool splitOnNewLine)
    {
        if (fullString.Length <= suffixLength)
            return fullString;

        var suffix = fullString.Substring(fullString.Length - suffixLength);
        if (splitOnNewLine) {
            var newlineIndex = suffix.IndexOf('\n');
            if (newlineIndex >= 0)
                return suffix.Substring(newlineIndex + 1);
        }

        return suffix;
    }

    [Fact]
    public void GetSuffix_WithSourcePrefix_ShouldHandlePartialPrefixMatch()
    {
        var sb = new StringBuilder();
        sb.Append("ABC");
        sb.Append("DEFGH");

        // Prefix doesn't match - should return from full content
        var result = sb.GetSuffix("XYZ", 5);
        result.Should().Be("DEFGH");

        // Partial prefix match at start
        sb.Clear();
        sb.Append("ABCDEFGH");
        result = sb.GetSuffix("ABC", 5);
        result.Should().Be("DEFGH");
    }

    [Fact]
    public void GetSuffix_WithSourcePrefix_ShouldHandleEmptyPrefix()
    {
        var sb = new StringBuilder();
        sb.Append("Content");

        var result = sb.GetSuffix("", 5);
        result.Should().Be("ntent");

        result = sb.GetSuffix("", 100);
        result.Should().Be("Content");
    }

    [Fact]
    public void GetSuffix_WithSourcePrefix_ShouldHandlePrefixEqualsContent()
    {
        var sb = new StringBuilder();
        var content = "SameContent";
        sb.Append(content);

        var result = sb.GetSuffix(content, content.Length);
        result.Should().Be(content);
        result = sb.GetSuffix(content, content.Length * 2);
        result.Should().Be(content + content);
        result = sb.GetSuffix(content, content.Length * 3);
        result.Should().Be(content + content);
    }

    [Fact]
    public void GetSuffix_WithSourcePrefix_ShouldBeConsistentWithMultipleCalls()
    {
        var sb = new StringBuilder();
        var random = new Random(123);
        var prefix = "SYSTEM_MESSAGE: ";

        sb.Append(prefix);

        // Build complex content
        for (int i = 0; i < 100; i++) {
            sb.Append(new string((char)('a' + random.Next(26)), random.Next(20, 50)));
            if (random.Next(3) == 0)
                sb.Append('\n');
        }

        var suffixLength = 500;

        // Multiple calls should return same result
        var result1 = sb.GetSuffix(prefix, suffixLength, splitOnNewLine: true);
        var result2 = sb.GetSuffix(prefix, suffixLength, splitOnNewLine: true);
        var result3 = sb.GetSuffix(prefix, suffixLength, splitOnNewLine: true);

        result1.Should().Be(result2);
        result2.Should().Be(result3);

        // Same for splitOnNewLine = false
        result1 = sb.GetSuffix(prefix, suffixLength, splitOnNewLine: false);
        result2 = sb.GetSuffix(prefix, suffixLength, splitOnNewLine: false);
        result3 = sb.GetSuffix(prefix, suffixLength, splitOnNewLine: false);

        result1.Should().Be(result2);
        result2.Should().Be(result3);
    }

    [Fact]
    public void GetSuffix_WithSourcePrefix_ShouldMatchWithoutPrefix_WhenPrefixIsEmpty()
    {
        var random = new Random(456);
        var sb = new StringBuilder();

        // Build test content
        for (int i = 0; i < 50; i++) {
            sb.Append(new string((char)('a' + random.Next(26)), random.Next(20, 50)));
            if (random.Next(3) == 0)
                sb.Append('\n');
        }

        var suffixLength = 500;

        // Results should match when sourcePrefix is empty
        var resultWithEmptyPrefix = sb.GetSuffix("", suffixLength, splitOnNewLine: true);
        var resultWithoutPrefix = sb.GetSuffix(suffixLength, splitOnNewLine: true);
        resultWithEmptyPrefix.Should().Be(resultWithoutPrefix);

        resultWithEmptyPrefix = sb.GetSuffix("", suffixLength, splitOnNewLine: false);
        resultWithoutPrefix = sb.GetSuffix(suffixLength, splitOnNewLine: false);
        resultWithEmptyPrefix.Should().Be(resultWithoutPrefix);
    }

    [Theory]
    [InlineData("Short", 10)]
    [InlineData("Medium length prefix here", 50)]
    [InlineData("A very long prefix that contains multiple words and characters", 100)]
    public void GetSuffix_WithSourcePrefix_ShouldExtractCorrectSuffix(string prefix, int suffixLength)
    {
        var sb = new StringBuilder();
        var content = new string('X', suffixLength * 2);

        sb.Append(prefix);
        sb.Append(content);

        var result = sb.GetSuffix(prefix, suffixLength);

        result.Length.Should().Be(suffixLength);
        result.Should().Be(content.Substring(content.Length - suffixLength));
        result.Should().NotContain(prefix);
    }

    [Fact]
    public void GetSuffix_WithSourcePrefix_ShouldHandleEdgeCases()
    {
        var sb = new StringBuilder();

        // Empty StringBuilder with prefix
        var result = sb.GetSuffix("prefix", 100);
        result.Should().Be("prefix");

        sb.Append("abc");
        result = sb.GetSuffix("12345", 50);
        result.Should().Be("12345abc");

        result = sb.GetSuffix("12345", 0);
        result.Should().Be("");
        result = sb.GetSuffix("12345", 1);
        result.Should().Be("c");
        result = sb.GetSuffix("12345", 3);
        result.Should().Be("abc");
        result = sb.GetSuffix("12345", 4);
        result.Should().Be("5abc");
        result = sb.GetSuffix("12345", 6);
        result.Should().Be("345abc");
    }
}
