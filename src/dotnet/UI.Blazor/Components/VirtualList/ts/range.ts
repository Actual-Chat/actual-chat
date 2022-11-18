export class Range<T> {
    constructor(
        public Start: T,
        public End: T,
    ) {
    }

    public get isEmpty(): boolean {
        return this.Start === this.End;
    }

    public equals(other?: Range<T>): boolean {
        return this.Start === other?.Start && this.End === other?.End;
    }
}

export class NumberRange extends Range<number> {

    constructor(Start: number, End: number) {
        super(Start, End);
    }

    public get size(): number {
        return this.End - this.Start;
    }

    public contains(containedRange: Range<number>): boolean {
        return this.Start <= containedRange.Start && containedRange.End <= this.End
    }

    public intersectWith(other: Range<number>): NumberRange {
        const start = Math.max(this.Start, other.Start);
        const end = Math.min(this.End, other.End);
        const result = new NumberRange(start, end);
        if (result.size < 0)
            return new NumberRange(0, 0);
        return result;
    }

    public fitInto(fitRange: NumberRange): NumberRange | null {
        const epsilon = 10;
        if (this.size > fitRange.size + epsilon)
            return null;
        if (this.End > fitRange.size + epsilon)
            return null;

        return new NumberRange(
            fitRange.Start + this.Start,
            fitRange.Start + this.End
        );
    }
}
