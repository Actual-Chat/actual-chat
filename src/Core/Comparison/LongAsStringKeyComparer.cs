using System;
using System.Collections.Generic;

namespace ActualChat.Comparison
{
    public class LongAsStringKeyComparer : IComparer<string>
    {
        public static IComparer<string> Default { get; } = new LongAsStringKeyComparer();

        public IComparer<string> BaseComparer { get; init; } = StringComparer.InvariantCulture;

        public int Compare(string? x, string? y)
            => long.TryParse(x, out var lx) && long.TryParse(y, out var ly)
                ? lx.CompareTo(ly)
                : BaseComparer.Compare(x, y);
    }
}
