import { initLogging, LogLevel, LogScope } from 'logging-init';
import 'logging-init';

export { LogLevel } from './logging-init';
export type { LogScope } from './logging-init';

export interface LogRef {
    target : unknown;
    id : number;
}

class LogRefQueue
{
    items : LogRef[];
    capacity : number;
    idSeed : number;

    constructor(capacity : number) {
        this.idSeed = 0;
        this.capacity = capacity;
        this.items = [];
    }

    public ref(data: unknown) : LogRef {
        const itemIndex = this.items.findIndex(el => el.target === data);
        if (itemIndex >= 0) {
            const existentItem = this.items[itemIndex];
            if (this.items.length > 1 && itemIndex < (this.items.length - 1)) {
                // move item to the beginning of the queue
                this.items.splice(itemIndex, 1);
                this.items.push(existentItem);
            }
            return existentItem;
        }
        else {
            // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
            const id = data['__logRefId'] as number ?? this.idSeed++;
            const newItem = { target: data, id: id };
            data['__logRefId'] = id;
            if (this.items.length >= this.capacity) {
                const deletedItems = this.items.splice(0, this.items.length - this.capacity + 1);
                for (const item of deletedItems) {
                    // clear log ref target to prevent memory leaks
                    // and keep string representation of the target for tracing
                    item.target = item.target.toString();
                }
            }
            this.items.push(newItem);
            return newItem;
        }
    }
}

export class Log {
    public static readonly minLevels: Map<LogScope, LogLevel> = new Map<LogScope, LogLevel>();
    public static defaultMinLevel = LogLevel.Info;
    private static isInitialized = false;
    private static logRefs : LogRefQueue = new LogRefQueue(5);
    public log: (...data: unknown[]) => void;

    constructor(
        public readonly scope: LogScope,
        public readonly level: LogLevel,
    ) {
        const prefix = `[${scope}]`;
        switch (level) {
            case LogLevel.Debug:
                this.log = (...data: unknown[]) => console.debug(prefix, ...data);
                break;
            case LogLevel.Info:
                this.log = (...data: unknown[]) => console.log(prefix, ...data);
                break;
            case LogLevel.Warn:
                this.log = (...data: unknown[]) => console.warn(prefix, ...data);
                break;
            case LogLevel.Error:
                this.log = (...data: unknown[]) => console.error(prefix, ...data);
                break;
            case LogLevel.None:
                throw new Error('LogLevel.None cannot be used here');
        }
    }

    public static loggerFactory = (scope: LogScope, level: LogLevel) => new Log(scope, level);

    public static get(scope: LogScope, level = LogLevel.Info): Log | null {
        if (!this.isInitialized) {
            this.isInitialized = true;
            initLogging(this);
        }

        const minLevels = this.minLevels;
        const minLevel = minLevels.get(scope)
            ?? minLevels.get('default')
            ?? this.defaultMinLevel;
        return level >= minLevel ? this.loggerFactory(scope, level) : null;
    }

    public static ref(data: unknown) : LogRef {
        return this.logRefs.ref(data);
    }

    public assert(predicate?: boolean, ...data: unknown[]): void {
        if (!predicate)
            this.log(data);
    }
}
