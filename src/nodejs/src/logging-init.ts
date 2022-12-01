// Should be the same as logging.LogLevel, but local to this module
enum LogLevel {
    Debug = 1,
    Info,
    Warn,
    Error,
    None = 1000,
}

const LogMinLevelsKey = 'logMinLevels';

export function initLogging(Log: unknown) : void {
    Log['defaultMinLevel'] = LogLevel.Debug;
    const minLevels = Log['minLevels'] as Map<string, LogLevel>;
    const w = globalThis;
    let wasRestored = false;

    if (w) {
        w[LogMinLevelsKey] = new LogMinLevels(minLevels);
        if (w.sessionStorage) {
            wasRestored = restoreFromStorage(w.sessionStorage, minLevels);
        }
    }

    if (!wasRestored)
        reset(minLevels);
}

export class LogMinLevels {
    constructor (private minLevels: Map<string, LogLevel>)
    { }

    public override(scope: string, newLevel: LogLevel): void {
        this.minLevels.set(scope, newLevel);

        const w = globalThis;
        if (w?.sessionStorage) {
            persistToStorage(w.sessionStorage, this.minLevels);
        }
    }

    public reset() {
        reset(this.minLevels);
    }
}

function restoreFromStorage(storage: Storage, minLevels: Map<string, LogLevel>): boolean {
    const w = globalThis;
    if (w?.sessionStorage) {
        const storedMinLevelsString = w.sessionStorage.getItem(LogMinLevelsKey);
        if (storedMinLevelsString) {
            const storedMinLevels = new Map(JSON.parse(storedMinLevelsString) as [string, LogLevel][]);
            if (!storedMinLevels.size || storedMinLevels.size == 0)
                return false;

            storedMinLevels.forEach((value, key) => minLevels.set(key, value));
            return true;
        }
    }
    return false;
}

function persistToStorage(storage: Storage, minLevels: Map<string, LogLevel>): void {
    storage.setItem(LogMinLevelsKey, JSON.stringify(Array.from(minLevels.entries())));
}

function reset (minLevels: Map<string, LogLevel>): void {
    // Bumping up levels of noisy scopes
    minLevels.set('NextInteraction', LogLevel.Info);
    minLevels.set('InteractiveUI', LogLevel.Info);
    minLevels.set('on-device-awake', LogLevel.Info);
    minLevels.set('Rpc', LogLevel.Info);
    minLevels.set('BrowserInfo', LogLevel.Info);
    minLevels.set('LocalSettings', LogLevel.Info);
    minLevels.set('UndoStack', LogLevel.Info);
    minLevels.set('MarkupEditor', LogLevel.Info);
    minLevels.set('ChatMessageEditor', LogLevel.Info);
    minLevels.set('WarmUpAudioWorkletProcessor', LogLevel.Info);
    minLevels.set('FeederProcessor', LogLevel.Info);
    minLevels.set('OpusEncoderWorker', LogLevel.Info);
    minLevels.set('OpusEncoderWorkletProcessor', LogLevel.Info);
    minLevels.set('OpusDecoder', LogLevel.Info);
    minLevels.set('OpusDecoderWorker', LogLevel.Info);
    minLevels.set('AudioPlayerController', LogLevel.Info);
    minLevels.set('AudioPlayer', LogLevel.Info);
    minLevels.set('UserActivityUI', LogLevel.Info);
    minLevels.set('MenuHost', LogLevel.Info);

    // Bumping down levels of in-dev scopes
    minLevels.set('AudioContextLazy', LogLevel.Debug);
    minLevels.set('AudioRecorder', LogLevel.Debug);
    minLevels.set('VirtualList', LogLevel.Info);

    // minLevels.clear(); // To quickly discard any tweaks :)

    const w = globalThis;
    if (w?.sessionStorage) {
        persistToStorage(w.sessionStorage, minLevels);
    }
}
