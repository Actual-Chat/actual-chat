import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'ModalHost';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class ModalHost {
    public static updateBodyStyle(hasModals: boolean) {
        document.body.style.overflow = hasModals ? 'hidden' : 'auto';
    }
}
