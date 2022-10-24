import { debounce, delayAsync, ResettableFunc, serialize, throttle } from '../../../../nodejs/src/promises';

const LogScope = 'JSTestPage';

export class JSTestPage {
    private static serialized: () => Promise<void>;
    private static serialized1: () => Promise<void>;
    private static serialized2: () => Promise<void>;
    private static throttled: ResettableFunc<() => void>;
    private static throttledSkip: ResettableFunc<() => void>;
    private static throttledDelayHead: ResettableFunc<() => void>;
    private static debounced: ResettableFunc<() => void>;
    private static debouncedHead: ResettableFunc<() => void>;

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
        this.throttled = throttle(loggerFactory("throttled"), 1000);
        this.throttledSkip = throttle(loggerFactory("throttled(mode = 'skip')"), 1000, 'skip');
        this.throttledDelayHead = throttle(loggerFactory("throttled(mode = 'delayHead')"), 1000, 'delayHead');
        this.debounced = debounce(loggerFactory("debounced"), 1000);
        this.debouncedHead = debounce(loggerFactory("debounced(debounceHead = true)"), 1000, true);
    }
}
