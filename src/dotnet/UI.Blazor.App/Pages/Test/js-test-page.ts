import { debounce, delayAsync, ResettableFunc, serialize, throttle } from 'promises';

export class JSTestPage {
    private static serialized: () => Promise<void>;
    private static serialized1: () => Promise<void>;
    private static serialized2: () => Promise<void>;
    private static throttled: ResettableFunc<() => void>;
    private static throttledSkip: ResettableFunc<() => void>;
    private static throttledDelayHead: ResettableFunc<() => void>;
    private static debounced: ResettableFunc<() => void>;
    private static debouncedHead: ResettableFunc<() => void>;
    private static throttled2: () => void;
    private static throttledSkip2: () => void;
    private static throttledDelayHead2: () => void;
    private static debounced2: () => void;
    private static debouncedHead2: () => void;

    public static init() {
        const loggerFactory = (name: string) => {
            let count = 0;
            return () => {
                const currentCount = ++count;
                console.log(`${name}(${currentCount})`);
            }
        }
        const asyncLoggerFactory = (name: string) => {
            let count = 0;
            return async () => {
                const currentCount = ++count;
                console.log(`${name}(${currentCount}): started`);
                await delayAsync(1000);
                console.log(`${name}(${currentCount}): completed`);
            }
        }
        this.serialized = serialize(asyncLoggerFactory("serialized"));
        this.serialized1 = serialize(asyncLoggerFactory("serialized1"), 1);
        this.serialized2 = serialize(asyncLoggerFactory("serialized2"), 2);
        this.throttledSkip = throttle(loggerFactory("throttled(mode = 'skip')"), 1000, 'skip', 'throttled-skip');
        this.throttledDelayHead = throttle(loggerFactory("throttled(mode = 'delayHead')"), 1000, 'delayHead', 'throrttled-delay-head');
        this.debounced = debounce(loggerFactory("debounced"), 1000,  'debounced');

        this.throttled2 = () => { this.throttled(); this.throttled(); }
        this.throttledSkip2 = () => { this.throttledSkip(); this.throttledSkip(); }
        this.throttledDelayHead2 = () => { this.throttledDelayHead(); this.throttledDelayHead(); }
        this.debounced2 = () => { this.debounced(); this.debounced(); }
        this.debouncedHead2 = () => { this.debouncedHead(); this.debouncedHead(); }
    }
}
