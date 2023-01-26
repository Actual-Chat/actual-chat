import { initLogging, LogScope, LogLevel } from 'logging-init';
import 'logging-init';

export { LogLevel } from './logging-init';
export type { LogScope } from './logging-init';

export class Log {
    public static readonly minLevels: Map<LogScope, LogLevel> = new Map<LogScope, LogLevel>();
    public static defaultMinLevel = LogLevel.Info;
    private static isInitialized = false;
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

    public assert(predicate?: boolean, ...data: unknown[]): void {
        if (!predicate)
            this.log(data);
    }
}
