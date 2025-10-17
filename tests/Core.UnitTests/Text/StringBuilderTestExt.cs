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
}
