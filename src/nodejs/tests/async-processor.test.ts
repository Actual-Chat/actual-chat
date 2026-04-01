import { describe, it, expect } from 'vitest';
import { AsyncProcessor } from 'async-processor';

describe('AsyncProcessor', () => {
    it('should process enqueued items in order', async () => {
        const processed: number[] = [];
        const processor = new AsyncProcessor<number>('test', (item) => {
            processed.push(item);
            return Promise.resolve(true);
        });

        processor.enqueue(1);
        processor.enqueue(2);
        processor.enqueue(3);

        // Give it time to process
        await new Promise(resolve => setTimeout(resolve, 50));

        expect(processed).toEqual([1, 2, 3]);
        expect(processor.isRunning).toBe(true);
    });

    it('should stop when process returns false', async () => {
        const processed: number[] = [];
        const processor = new AsyncProcessor<number>('test-stop', (item) => {
            processed.push(item);
            return Promise.resolve(item !== 2); // stop at item 2
        });

        processor.enqueue(1);
        processor.enqueue(2);
        processor.enqueue(3); // should not be processed

        await processor.whenRunning;

        expect(processed).toEqual([1, 2]);
        expect(processor.isRunning).toBe(false);
    });

    it('should throw when enqueuing after stop', async () => {
        const processor = new AsyncProcessor<number>('test-throw', () => Promise.resolve(false));
        processor.enqueue(1);
        await processor.whenRunning;

        expect(() => processor.enqueue(2)).toThrow('already stopping or stopped');
    });

    it('should silently ignore enqueue after stop with mustFailIfAlreadyStopped=false', async () => {
        const processor = new AsyncProcessor<number>('test-silent', () => Promise.resolve(false));
        processor.enqueue(1);
        await processor.whenRunning;

        // Should not throw
        processor.enqueue(2, false);
    });

    it('should handle async processing', async () => {
        const processed: string[] = [];
        const processor = new AsyncProcessor<string>('test-async', async (item) => {
            await new Promise(resolve => setTimeout(resolve, 10));
            processed.push(item);
            return true;
        });

        processor.enqueue('a');
        processor.enqueue('b');

        await new Promise(resolve => setTimeout(resolve, 100));

        expect(processed).toEqual(['a', 'b']);
    });

    it('should wait for items when queue is empty', async () => {
        const processed: number[] = [];
        const processor = new AsyncProcessor<number>('test-wait', (item) => {
            processed.push(item);
            return Promise.resolve(true);
        });

        // Wait, then enqueue
        await new Promise(resolve => setTimeout(resolve, 30));
        processor.enqueue(42);
        await new Promise(resolve => setTimeout(resolve, 30));

        expect(processed).toEqual([42]);
    });

    it('should clear queue', async () => {
        const processed: number[] = [];
        const gate = new Promise<void>(resolve => {
            const processor = new AsyncProcessor<number>('test-clear', async (item) => {
                if (item === 1) {
                    // While processing item 1, clear the rest
                    await new Promise(r => setTimeout(r, 20));
                    processor.clearQueue();
                    processor.enqueue(99);
                }
                processed.push(item);
                if (item === 99) resolve();
                return true;
            });

            processor.enqueue(1);
            processor.enqueue(2);
            processor.enqueue(3);
        });

        await gate;
        expect(processed).toEqual([1, 99]); // 2 and 3 were cleared
    });
});
