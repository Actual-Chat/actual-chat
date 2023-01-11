import { initLogging } from 'logging-init';
import 'logging-init';

export enum LogLevel {
    Debug = 1,
    Info,
    Warn,
    Error,
    None = 1000,
}

export class Log {
    public static readonly minLevels: Map<string, LogLevel> = new Map<string, LogLevel>();
    public static defaultMinLevel = LogLevel.Error;
    private static isInitialized = false;
    public log: (...data: unknown[]) => void;

    constructor(
        public readonly scope: string,
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

    public static loggerFactory = (scope: string, level: LogLevel) => new Log(scope, level);

    public static get(scope: string, level = LogLevel.Info): Log | null {
        if (!this.isInitialized) {
            this.isInitialized = true;
            initLogging(this);
        }
        const minLevel = this.minLevels.get(scope) ?? this.defaultMinLevel;
        return level >= minLevel ? this.loggerFactory(scope, level) : null;
    }

    public assert(predicate?: boolean, ...data: unknown[]): void {
        if (!predicate)
            this.log(data);
    }
}
