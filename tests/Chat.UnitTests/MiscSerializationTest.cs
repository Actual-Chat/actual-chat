namespace ActualChat.Chat.UnitTests;

public class MiscSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ImmutableOptionSetTest1()
    {
        byte[]? lastData = null;
        for (var i = 0; i < 10_000; i++) {
            var items = Enumerable.Range(0, 20).Select(x => x.Format()).Shuffle().ToArray();
            var s = new ImmutableOptionSet();
            foreach (var item in items)
                s = s.Set(item, item);
            var data = MemoryPackByteSerializer.Default.Write(s).WrittenSpan.ToArray();
            if (lastData != null)
                data.SequenceEqual(lastData).Should().BeTrue();
            lastData = data;
        }
    }

    [Fact(Skip = "Super slow, run it manually")]
    public void ImmutableOptionSetTest2()
    {
        var s1 = "a";
        var s2 = FindSameHashedString(s1);
        if (s2 == null)
            return;

        Out.WriteLine($"Same hashed strings: '{s1}', '{s2}'");
        var s = new ImmutableOptionSet();
        byte[]? lastData = null;
        for (var i = 0; i < 2; i++) {
            var items = i == 0 ? (string[])[s1, s2] : [s2, s1];
            foreach (var item in items)
                s = s.Set(item, item);
            var data = MemoryPackByteSerializer.Default.Write(s).WrittenSpan.ToArray();
            if (lastData != null)
                data.SequenceEqual(lastData).Should().BeTrue();
            lastData = data;
        }
    }

    private static string? FindSameHashedString(string s)
    {
        var h = s.GetHashCode(StringComparison.Ordinal);
        for (var i = 0L; i < (long)int.MaxValue * 2; i++) {
            var s1 = i.Format();
            if (s1.GetHashCode(StringComparison.Ordinal) == h)
                return s1;
        }
        return null;
    }
}
