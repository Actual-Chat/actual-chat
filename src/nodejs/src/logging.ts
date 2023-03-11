import { initLogging, LogLevel, LogScope } from 'logging-init';
import 'logging-init';

export { LogLevel } from './logging-init';
export type { LogScope } from './logging-init';

export interface LogRef {
    target : unknown;
    id : number;
}

interface SetItem {
    ref : LogRef;
    touchedAt : number;
}

class LogRefSet {
    items : SetItem[];
    capacity : number;
    idSeed : number;

    constructor(capacity : number) {
        this.idSeed = 0;
        this.capacity = capacity;
        this.items = [];
    }

    public ref(data: object) : LogRef {
        const itemIndex = this.items.findIndex(el => el.ref.target === data);
        if (itemIndex >= 0) {
            const existentItem = this.items[itemIndex];
            existentItem.touchedAt = Date.now();
            return existentItem.ref;
        }
        else {
            // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
            const id = data['__logRefId'] as number ?? this.idSeed++;
            const newRef = { target: data, id: id };
            data['__logRefId'] = id;
            if (this.items.length >= this.capacity)
                this.removeOldest();
            const newItem = { ref : newRef, touchedAt : Date.now() };
            this.items.push(newItem);
            return newRef;
        }
    }

    private removeOldest() {
        let indexToEliminate = 0;
        let itemToEliminate = this.items[0];
        for (let i = 1; i < this.items.length; i++) {
            const item = this.items[i];
            if (item.touchedAt < itemToEliminate.touchedAt) {
                itemToEliminate = item;
                indexToEliminate = i;
            }
        }
        this.items.splice(indexToEliminate, 1);
        // clear log ref target to prevent memory leaks
        // and keep string representation of the target for tracing
        const ref = itemToEliminate.ref;
        ref.target = ref.target.toString();
    }
}

export class Log {
    private static isInitialized = false;
    private static logRefs : LogRefSet = new LogRefSet(10);
    public static readonly minLevels: Map<LogScope, LogLevel> = new Map<LogScope, LogLevel>();
    public static defaultMinLevel = LogLevel.Info;
    public log: (...data: unknown[]) => void;
    public trace: (...data: unknown[]) => void;

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
        this.trace = (...data: unknown[]) => console.trace(prefix, ...data);
    }

    public static loggerFactory = (scope: LogScope, level: LogLevel) => new Log(scope, level);

    public static get(scope: LogScope) {
        if (!this.isInitialized) {
            this.isInitialized = true;
            initLogging(this);
        }

        const minLevels = this.minLevels;
        const minLevel = minLevels.get(scope)
            ?? minLevels.get('default')
            ?? this.defaultMinLevel;

        const getLogger = (level: LogLevel) => level >= minLevel ? this.loggerFactory(scope, level) : null;

        return {
            logScope: scope,
            debugLog: getLogger(LogLevel.Debug),
            infoLog: getLogger(LogLevel.Info),
            warnLog: getLogger(LogLevel.Warn),
            errorLog: getLogger(LogLevel.Error),
        };
    }

    public static ref(data: object) : object {
        if (!data)
            return data;
        return this.logRefs.ref(data);
    }

    public assert(predicate?: boolean, ...data: unknown[]): void {
        if (!predicate)
            this.log(data);
    }
}
