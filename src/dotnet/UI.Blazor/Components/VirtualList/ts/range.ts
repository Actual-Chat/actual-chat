export class Range<T> {
    constructor(
        public start: T,
        public end: T,
    ) {
    }

    public get isEmpty(): boolean {
        return this.start === this.end;
    }

    public equals(other?: Range<T>): boolean {
        return this.start === other?.start && this.end === other?.end;
    }
}

export class NumberRange extends Range<number> {
    constructor(start: number, end: number) {
        super(start, end);
    }

    public get size(): number { return this.end - this.start; }

    public contains(containedRange: Range<number>): boolean;
    public contains(item: number): boolean;
    public contains(item: Range<number> | number): boolean {
        if (item == null)
            return false;

        if (typeof item === 'number')
            return this.start <= item && item <= this.end;

        return this.start <= item.start && item.end <= this.end;
    }

    public intersectWith(other: Range<number>): NumberRange {
        const start = Math.max(this.start, other.start);
        const end = Math.min(this.end, other.end);
        const result = new NumberRange(start, end);
        if (result.size < 0)
            return new NumberRange(0, 0);
        return result;
    }

    public fitInto(fitRange: NumberRange): NumberRange | null {
        const epsilon = 10;
        if (this.size > fitRange.size + epsilon)
            return fitRange;
        if (this.end > fitRange.size + epsilon)
            return null;

        return new NumberRange(
            fitRange.start + this.start,
            fitRange.start + this.end
        );
    }
}
