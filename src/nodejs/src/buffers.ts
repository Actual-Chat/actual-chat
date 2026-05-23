export type DisposeAction<T> = (item: T) => void;

export interface ReplaceableSlotOptions<T> {
    dispose?: DisposeAction<T>;
}

function noopDispose(_item: unknown): void {
    return;
}

/** A capacity-1 slot where newer items replace older pending items. */
export class ReplaceableSlot<T> {
    private readonly disposeItem: DisposeAction<T>;
    private item: T | null = null;
    private _replacementCount = 0;

    get value(): T | null { return this.item; }
    get hasValue(): boolean { return this.item !== null; }
    get length(): number { return this.item === null ? 0 : 1; }
    get replacementCount(): number { return this._replacementCount; }

    constructor(options?: ReplaceableSlotOptions<T>) {
        this.disposeItem = options?.dispose ?? noopDispose;
    }

    isEmpty(): boolean {
        return this.item === null;
    }

    push(item: T): void {
        if (this.item !== null) {
            this._replacementCount++;
            this.disposeItem(this.item);
        }
        this.item = item;
    }

    replace(item: T): void {
        this.push(item);
    }

    take(): T | null {
        const item = this.item;
        this.item = null;
        return item;
    }

    shift(): T | undefined {
        return this.take() ?? undefined;
    }

    peekFront(): T | undefined {
        return this.item ?? undefined;
    }

    peekBack(): T | undefined {
        return this.item ?? undefined;
    }

    peekAt(index: number): T | undefined {
        return index === 0 ? this.item ?? undefined : undefined;
    }

    clear(): void {
        if (this.item === null)
            return;

        const item = this.item;
        this.item = null;
        this.disposeItem(item);
    }

    resetStats(): void {
        this._replacementCount = 0;
    }
}

export interface BoundedQueueOptions<T> {
    capacity: number;
    dispose?: DisposeAction<T>;
}

/** A capacity-N FIFO that drops the oldest item when full. */
export class BoundedQueue<T> {
    readonly capacity: number;
    private readonly disposeItem: DisposeAction<T>;
    private readonly items: T[] = [];
    private _droppedCount = 0;

    get length(): number { return this.items.length; }
    get droppedCount(): number { return this._droppedCount; }
    get isEmpty(): boolean { return this.items.length === 0; }
    get isFull(): boolean { return this.items.length >= this.capacity; }

    constructor(options: BoundedQueueOptions<T>) {
        if (!Number.isInteger(options.capacity) || options.capacity < 1)
            throw new RangeError(
                `BoundedQueue capacity must be a positive integer (was ${options.capacity}).`);
        this.capacity = options.capacity;
        this.disposeItem = options.dispose ?? noopDispose;
    }

    push(item: T): void {
        while (this.items.length >= this.capacity) {
            this._droppedCount++;
            const oldest = this.items.shift()!;
            this.disposeItem(oldest);
        }
        this.items.push(item);
    }

    take(): T | null {
        return this.items.shift() ?? null;
    }

    clear(): void {
        while (this.items.length > 0) {
            const item = this.items.shift()!;
            this.disposeItem(item);
        }
    }

    resetStats(): void {
        this._droppedCount = 0;
    }
}

export interface OwnedArrayBufferStats {
    readonly fastCount: number;
    readonly slowCount: number;
    readonly totalCount: number;
    readonly fastRatio: number;
}

/** Tracks whether byte views can be reused as owned buffers or must be copied. */
export class OwnedArrayBufferTracker {
    private _fastCount = 0;
    private _slowCount = 0;

    get stats(): OwnedArrayBufferStats {
        const totalCount = this._fastCount + this._slowCount;
        return {
            fastCount: this._fastCount,
            slowCount: this._slowCount,
            totalCount,
            fastRatio: totalCount === 0 ? 0 : this._fastCount / totalCount,
        };
    }

    get(view: Uint8Array): ArrayBuffer {
        const buffer = view.buffer;
        const isOwned =
            buffer instanceof ArrayBuffer
            && view.byteOffset === 0
            && view.byteLength === buffer.byteLength;
        if (isOwned) {
            this._fastCount++;
            return buffer;
        }

        this._slowCount++;
        return view.slice().buffer;
    }

    reset(): void {
        this._fastCount = 0;
        this._slowCount = 0;
    }
}

export function ownedArrayBuffer(view: Uint8Array): ArrayBuffer {
    const buffer = view.buffer;
    if (
        buffer instanceof ArrayBuffer
        && view.byteOffset === 0
        && view.byteLength === buffer.byteLength
    )
        return buffer;

    return view.slice().buffer;
}

export type BinaryBufferSource = ArrayBufferLike | ArrayBufferView;

/** A fixed-capacity byte ring buffer. */
export class BinaryRingBuffer {
    private readonly buffer: Uint8Array;
    private start = 0;
    private end = 0;
    private _count = 0;

    readonly capacity: number;

    get count(): number { return this._count; }
    get remainingCapacity(): number { return this.capacity - this._count; }
    get isEmpty(): boolean { return this._count === 0; }
    get isFull(): boolean { return this._count === this.capacity; }

    constructor(capacity: number) {
        if (!Number.isInteger(capacity) || capacity < 1)
            throw new RangeError(`BinaryRingBuffer capacity must be a positive integer (was ${capacity}).`);

        this.capacity = capacity;
        this.buffer = new Uint8Array(capacity);
    }

    push(source: BinaryBufferSource): void {
        const bytes = toUint8Array(source);
        if (bytes.byteLength > this.remainingCapacity)
            throw new Error('BinaryRingBuffer does not have enough remaining capacity.');

        this.write(bytes);
    }

    pushAndMoveHeadIfFull(source: BinaryBufferSource): number {
        let bytes = toUint8Array(source);
        let skipped = 0;
        if (bytes.byteLength > this.capacity) {
            skipped = this._count + bytes.byteLength - this.capacity;
            bytes = bytes.subarray(bytes.byteLength - this.capacity);
            this.clear();
        } else if (bytes.byteLength > this.remainingCapacity) {
            skipped = bytes.byteLength - this.remainingCapacity;
            this.moveHead(skipped);
        }

        this.write(bytes);
        return skipped;
    }

    pull(target: Uint8Array): boolean {
        if (target.byteLength > this._count)
            return false;

        this.read(target);
        return true;
    }

    peek(target: Uint8Array, offset = 0): boolean {
        if (offset < 0 || target.byteLength + offset > this._count)
            return false;

        this.copyOut(target, (this.start + offset) % this.capacity);
        return true;
    }

    moveHead(skipCount: number): void {
        if (!Number.isInteger(skipCount) || skipCount < 0 || skipCount > this._count)
            throw new RangeError(`skipCount ${skipCount} out of range [0, ${this._count}].`);
        if (skipCount === 0)
            return;

        this.start = (this.start + skipCount) % this.capacity;
        this._count -= skipCount;
        if (this._count === 0)
            this.end = this.start;
    }

    clear(): void {
        this.start = 0;
        this.end = 0;
        this._count = 0;
    }

    toArray(): Uint8Array {
        const result = new Uint8Array(this._count);
        this.peek(result);
        return result;
    }

    private write(bytes: Uint8Array): void {
        this.copyIn(bytes, this.end);
        this.end = (this.end + bytes.byteLength) % this.capacity;
        this._count += bytes.byteLength;
    }

    private read(target: Uint8Array): void {
        this.copyOut(target, this.start);
        this.start = (this.start + target.byteLength) % this.capacity;
        this._count -= target.byteLength;
        if (this._count === 0)
            this.end = this.start;
    }

    private copyIn(source: Uint8Array, targetOffset: number): void {
        const firstPartLength = Math.min(source.byteLength, this.capacity - targetOffset);
        this.buffer.set(source.subarray(0, firstPartLength), targetOffset);
        if (firstPartLength < source.byteLength)
            this.buffer.set(source.subarray(firstPartLength), 0);
    }

    private copyOut(target: Uint8Array, sourceOffset: number): void {
        const firstPartLength = Math.min(target.byteLength, this.capacity - sourceOffset);
        target.set(this.buffer.subarray(sourceOffset, sourceOffset + firstPartLength), 0);
        if (firstPartLength < target.byteLength)
            target.set(this.buffer.subarray(0, target.byteLength - firstPartLength), firstPartLength);
    }
}

function toUint8Array(source: BinaryBufferSource): Uint8Array {
    if (ArrayBuffer.isView(source))
        return new Uint8Array(source.buffer, source.byteOffset, source.byteLength);
    return new Uint8Array(source);
}
