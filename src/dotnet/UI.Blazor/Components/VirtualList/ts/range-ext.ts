import { Range } from './range';

export class RangeExt {
    public static Size(range: Range<number>): number {
        return range.End - range.Start;
    }

    public static Contains(range: Range<number>, containedRange: Range<number>): boolean {
        return range.Start <= containedRange.Start && containedRange.End <= range.End
    }

    public static IntersectWith(range: Range<number>, other: Range<number>): Range<number> {
        const start = Math.max(range.Start, other.Start);
        const end = Math.min(range.End, other.End);
        const result = new Range(start, end);
        if (this.Size(result) < 0)
            return new Range(0, 0);
        return result;
    }

    public static ScrollInto(
        range: Range<number>,
        fitRange: Range<number>,
        isEndAligned: boolean = false): Range<number> {
        if (this.Contains(range, fitRange))
            return range;

        const size = this.Size(range);
        if (isEndAligned) {
            if (range.Start < fitRange.Start)
                return new Range(fitRange.Start, size);
            if (range.End > fitRange.End)
                return new Range(fitRange.End - size, fitRange.End);
        } else {
            if (range.End > fitRange.End)
                return new Range(fitRange.End - size, fitRange.End);
            if (range.Start < fitRange.Start)
                return new Range(fitRange.Start, size);
        }

        return range;
    }
}
