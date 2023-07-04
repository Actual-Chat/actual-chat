import Heap from 'heap-js';
import { Log } from 'logging';

const { errorLog } = Log.get('TimerQueue');

export class TimerQueueTimer {
    constructor(
        public readonly handle: number,
        public callback: () => unknown,
        public readonly time: number) {
    }

    public clear() {
        this.callback = null;
    }
}

let nextHandle = 1;

export class TimerQueue {
    private readonly map = new Map<number, TimerQueueTimer>();
    private readonly heap = new Heap<TimerQueueTimer>((a: TimerQueueTimer, b: TimerQueueTimer) => b.time - a.time);

    public enqueue(delayMs: number, callback: () => unknown): TimerQueueTimer {
        const handle = nextHandle++;
        if (handle & 15) {
            // We want to make sure expired timers trigger even if triggerExpired()
            // somehow isn't invoked explicitly.
            this.triggerExpired();
        }

        const now = Date.now();
        const timer = new TimerQueueTimer(handle, callback, now + delayMs);
        this.map.set(timer.handle, timer);
        this.heap.add(timer);
        return timer;
    }

    public get(handle: number): TimerQueueTimer | undefined {
        return this.map.get(handle);
    }

    public readonly triggerExpired = (): void =>  {
        const now = Date.now();
        for (;;) {
            const timer = this.heap.peek();
            if (!timer || timer.time > now)
                break;
            this.heap.pop();
            this.map.delete(timer.handle);
            if (!timer.callback)
                continue

            try {
                timer.callback();
            }
            catch (e) {
                errorLog?.log('Callback failed:', e);
            }
        }
    }

    // setTimeout / clearTimeout

    public readonly setTimeout = (callback: () => unknown, delayMs: number): number => {
        return this.enqueue(delayMs, callback).handle;
    }

    public readonly clearTimeout = (handle: number): void => {
        this.get(handle)?.clear();
    }
}

const setTimeoutImpl = globalThis['setTimeout'] as (callback: () => unknown, delayMs: number) => number;
const clearTimeoutImpl = globalThis['clearTimeout'] as (handle: number) => void;

export const timerQueue = !setTimeoutImpl ? new TimerQueue() : null;
export const setTimeout = timerQueue ? timerQueue.setTimeout : setTimeoutImpl;
export const clearTimeout = timerQueue ? timerQueue.clearTimeout : clearTimeoutImpl;
