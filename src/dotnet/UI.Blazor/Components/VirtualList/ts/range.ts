export class Range<T> {
    constructor(
        public Start: T,
        public End: T,
    ) {
    }

    public equals(other?: Range<T>): boolean {
        return this.Start === other?.Start && this.End === other?.End;
    }
}
