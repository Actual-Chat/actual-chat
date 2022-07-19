import { Range } from './range';

export class RangeExt {
    public static size(range: Range<number>): number {
        return range.End - range.Start;
    }

    public static contains(range: Range<number>, containedRange: Range<number>): boolean {
        return range.Start <= containedRange.Start && containedRange.End <= range.End
    }

    public static intersectsWith(range: Range<number>, other: Range<number>): Range<number> {
        const start = Math.max(range.Start, other.Start);
        const end = Math.min(range.End, other.End);
        const result = new Range(start, end);
        if (this.size(result) < 0)
            return new Range(0, 0);
        return result;
    }

    public static scrollInto(
        range: Range<number>,
        fitRange: Range<number>,
        isEndAligned: boolean = false): Range<number> {
        if (this.contains(range, fitRange))
            return range;

        const size = this.size(range);
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
