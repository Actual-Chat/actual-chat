using System.Collections.Generic;
using Stl;

namespace ActualChat.Mathematics
{
    public interface ILogCover<TPoint, out TSize>
        where TPoint : notnull
        where TSize : notnull
    {
        TPoint Zero { get; }
        TSize MinSpanSize { get; }
        TSize MaxSpanSize { get; }
        int SpanSizeMultiplier { get; }
        TSize[] SpanSizes { get; }

        public bool IsValidSpan(TPoint start, TPoint end);
        TPoint GetSpanStart(TPoint innerPoint, int spanSizeIndex);
        IEnumerable<(TPoint Start, TPoint End)> GetSpans(TPoint innerPoint);
        Option<(TPoint Start, TPoint End)> TryGetSpan(TPoint maxStart, TPoint minEnd);
        (TPoint Min, TPoint Max) GetSpan(TPoint maxStart, TPoint minEnd);
    }
}
