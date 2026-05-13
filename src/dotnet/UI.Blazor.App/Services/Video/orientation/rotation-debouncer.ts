import type { RotationQuarter } from './quantize';

export class RotationDebouncer {
    private committedValue: RotationQuarter;
    private candidate: RotationQuarter;
    private candidateSinceMs: number | null = null;
    private justChangedFlag = false;

    constructor(initial: RotationQuarter, private readonly dwellMs: number) {
        this.committedValue = initial;
        this.candidate = initial;
    }

    get committed(): RotationQuarter {
        return this.committedValue;
    }

    get justChanged(): boolean {
        return this.justChangedFlag;
    }

    feed(target: RotationQuarter, nowMs: number): RotationQuarter {
        if (this.justChangedFlag)
            this.justChangedFlag = false;

        if (target === this.committedValue) {
            this.candidate = target;
            this.candidateSinceMs = null;
            return this.committedValue;
        }

        if (target !== this.candidate) {
            this.candidate = target;
            this.candidateSinceMs = nowMs;
            return this.committedValue;
        }

        if (this.candidateSinceMs === null) {
            this.candidateSinceMs = nowMs;
            return this.committedValue;
        }

        if (nowMs - this.candidateSinceMs >= this.dwellMs) {
            this.committedValue = target;
            this.candidateSinceMs = null;
            this.justChangedFlag = true;
        }
        return this.committedValue;
    }
}
