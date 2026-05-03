import { describe, it, expect } from 'vitest';
import {
    BinaryRingBuffer,
    OwnedArrayBufferTracker,
    ReplaceableSlot,
    ownedArrayBuffer,
} from 'buffers';

describe('ReplaceableSlot', () => {
    it('should store and take the current item', () => {
        const slot = new ReplaceableSlot<number>();

        slot.push(1);

        expect(slot.hasValue).toBe(true);
        expect(slot.length).toBe(1);
        expect(slot.value).toBe(1);
        expect(slot.take()).toBe(1);
        expect(slot.isEmpty()).toBe(true);
    });

    it('should dispose replaced items', () => {
        const disposed: number[] = [];
        const slot = new ReplaceableSlot<{ id: number }>({
            dispose: item => disposed.push(item.id),
        });

        slot.push({ id: 1 });
        slot.push({ id: 2 });
        slot.push({ id: 3 });

        expect(disposed).toEqual([1, 2]);
        expect(slot.replacementCount).toBe(2);
        expect(slot.value?.id).toBe(3);
    });

    it('should not dispose item transferred by take', () => {
        const disposed: number[] = [];
        const slot = new ReplaceableSlot<{ id: number }>({
            dispose: item => disposed.push(item.id),
        });

        slot.push({ id: 1 });
        const item = slot.take();
        slot.clear();

        expect(item?.id).toBe(1);
        expect(disposed).toEqual([]);
    });

    it('should expose Denque-like peek and shift helpers', () => {
        const slot = new ReplaceableSlot<string>();

        slot.push('a');

        expect(slot.peekFront()).toBe('a');
        expect(slot.peekBack()).toBe('a');
        expect(slot.peekAt(0)).toBe('a');
        expect(slot.peekAt(1)).toBeUndefined();
        expect(slot.shift()).toBe('a');
        expect(slot.shift()).toBeUndefined();
    });
});

describe('ownedArrayBuffer', () => {
    it('should return the original ArrayBuffer for owned views', () => {
        const source = new Uint8Array([1, 2, 3]);

        const result = ownedArrayBuffer(source);

        expect(result).toBe(source.buffer);
    });

    it('should copy subarray views', () => {
        const source = new Uint8Array([1, 2, 3, 4]);
        const view = source.subarray(1, 3);

        const result = ownedArrayBuffer(view);
        const bytes = new Uint8Array(result);

        expect(result).not.toBe(source.buffer);
        expect([...bytes]).toEqual([2, 3]);
    });

    it('should track fast and slow ownership paths', () => {
        const tracker = new OwnedArrayBufferTracker();
        const owned = new Uint8Array([1]);
        const sliced = new Uint8Array([1, 2, 3]).subarray(1);

        tracker.get(owned);
        tracker.get(sliced);

        expect(tracker.stats).toEqual({
            fastCount: 1,
            slowCount: 1,
            totalCount: 2,
            fastRatio: 0.5,
        });
    });
});

describe('BinaryRingBuffer', () => {
    it('should push and pull bytes in order', () => {
        const buffer = new BinaryRingBuffer(8);
        const target = new Uint8Array(3);

        buffer.push(new Uint8Array([1, 2, 3]));

        expect(buffer.count).toBe(3);
        expect(buffer.pull(target)).toBe(true);
        expect([...target]).toEqual([1, 2, 3]);
        expect(buffer.isEmpty).toBe(true);
    });

    it('should preserve order across wraparound', () => {
        const buffer = new BinaryRingBuffer(5);
        const first = new Uint8Array(3);
        const second = new Uint8Array(4);

        buffer.push(new Uint8Array([1, 2, 3, 4]));
        expect(buffer.pull(first)).toBe(true);
        buffer.push(new Uint8Array([5, 6, 7, 8]));

        expect(buffer.pull(second)).toBe(true);
        expect([...second]).toEqual([4, 5, 6, 7]);
        expect([...buffer.toArray()]).toEqual([8]);
    });

    it('should reject pushes that exceed remaining capacity', () => {
        const buffer = new BinaryRingBuffer(3);

        buffer.push(new Uint8Array([1, 2]));

        expect(() => buffer.push(new Uint8Array([3, 4]))).toThrow(
            'BinaryRingBuffer does not have enough remaining capacity.');
    });

    it('should drop oldest bytes when requested', () => {
        const buffer = new BinaryRingBuffer(5);

        buffer.push(new Uint8Array([1, 2, 3, 4]));
        const skipped = buffer.pushAndMoveHeadIfFull(new Uint8Array([5, 6, 7]));

        expect(skipped).toBe(2);
        expect([...buffer.toArray()]).toEqual([3, 4, 5, 6, 7]);
    });

    it('should keep only the source tail when source is larger than capacity', () => {
        const buffer = new BinaryRingBuffer(4);

        buffer.push(new Uint8Array([1, 2]));
        const skipped = buffer.pushAndMoveHeadIfFull(new Uint8Array([3, 4, 5, 6, 7]));

        expect(skipped).toBe(3);
        expect([...buffer.toArray()]).toEqual([4, 5, 6, 7]);
    });

    it('should peek without consuming', () => {
        const buffer = new BinaryRingBuffer(5);
        const target = new Uint8Array(2);

        buffer.push(new Uint8Array([1, 2, 3]));

        expect(buffer.peek(target, 1)).toBe(true);
        expect([...target]).toEqual([2, 3]);
        expect([...buffer.toArray()]).toEqual([1, 2, 3]);
    });
});
