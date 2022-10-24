import { Log, LogLevel } from 'logging';

export class ConsoleLog extends Log {
    constructor(
        public readonly scope: string,
        public readonly level: LogLevel,
    ) {
        super(scope, level);
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
}
