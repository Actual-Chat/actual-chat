export enum LogLevel {
    Debug = 1,
    Info,
    Warn,
    Error,
    None = 1000,
}

export class Log {
    public static readonly minLevels: Map<string, LogLevel> = new Map<string, LogLevel>();
    public static defaultMinLevel: LogLevel = LogLevel.Error;
    public static loggerFactory: (scope: string, level: LogLevel) => Log;

    constructor(
        public readonly scope: string,
        public readonly level: LogLevel,
    ) { }

    public static get(scope: string, level = LogLevel.Info) : Log | null {
        const minLevel = this.minLevels.get(scope) ?? this.defaultMinLevel;
        return level >= minLevel ? this.loggerFactory(scope, level) : null;
    }

    public log: (...data: unknown[]) => void;

    public assert(predicate?: boolean, ...data: unknown[]) : void {
        if (!predicate)
            this.log(data);
    }
}
