// Should be the same as logging.LogLevel, but local to this module
enum LogLevel {
    Debug = 1,
    Info,
    Warn,
    Error,
    None = 1000,
}

const LogMinLevelsKey = 'logMinLevels';

export function initLogging(Log: unknown): void {
    Log['defaultMinLevel'] = LogLevel.Debug;
    const minLevels = Log['minLevels'] as Map<string, LogLevel>;

    let wasRestored = false;
    if (globalThis) {
        globalThis[LogMinLevelsKey] = new LogMinLevels(minLevels);
        wasRestored = restore(minLevels);
    }
    if (!wasRestored)
        reset(minLevels);
}

export class LogMinLevels {
    constructor (private minLevels: Map<string, LogLevel>)
    { }

    public override(scope: string, newLevel: LogLevel): void {
        this.minLevels.set(scope, newLevel);
        persist(this.minLevels);
    }

    public reset() {
        reset(this.minLevels);
        persist(this.minLevels)
    }

    public clear(defaultLevel?: LogLevel) {
        this.minLevels.clear();
        if (defaultLevel !== undefined)
            this.minLevels['default'] = defaultLevel;
        persist(this.minLevels)
    }
}

function restore(minLevels: Map<string, LogLevel>): boolean {
    const storage = globalThis?.sessionStorage;
    if (!storage)
        return false;

    const readJson = storage.getItem(LogMinLevelsKey);
    if (!readJson)
        return false;

    const readMinLevels = new Map(JSON.parse(readJson) as [string, LogLevel][]);
    if (!(typeof readMinLevels.size === 'number'))
        return false;

    minLevels.clear();
    readMinLevels.forEach((value, key) => minLevels.set(key, value));
    return true;
}

function persist(minLevels: Map<string, LogLevel>): boolean {
    const storage = globalThis?.sessionStorage;
    if (!storage)
        return false;

    storage.setItem(LogMinLevelsKey, JSON.stringify(Array.from(minLevels.entries())));
    return true;
}

function reset(minLevels: Map<string, LogLevel>): void {
    // Bumping up levels of noisy scopes
    minLevels.set('LongPress', LogLevel.Info);
    minLevels.set('ScreenSize', LogLevel.Info);
    minLevels.set('NextInteraction', LogLevel.Info);
    minLevels.set('HistoryUI', LogLevel.Info);
    minLevels.set('InteractiveUI', LogLevel.Info);
    minLevels.set('TuneUI', LogLevel.Info);
    minLevels.set('VibrationUI', LogLevel.Info);
    minLevels.set('on-device-awake', LogLevel.Info);
    minLevels.set('Rpc', LogLevel.Info);
    minLevels.set('BrowserInfo', LogLevel.Info);
    minLevels.set('LocalSettings', LogLevel.Info);
    minLevels.set('UndoStack', LogLevel.Info);
    minLevels.set('MarkupEditor', LogLevel.Info);
    minLevels.set('ChatMessageEditor', LogLevel.Info);
    minLevels.set('WarmUpAudioWorkletProcessor', LogLevel.Info);
    minLevels.set('FeederProcessor', LogLevel.Info);
    minLevels.set('OpusEncoderWorker', LogLevel.Debug);
    minLevels.set('OpusEncoderWorkletProcessor', LogLevel.Info);
    minLevels.set('OpusDecoder', LogLevel.Info);
    minLevels.set('OpusDecoderWorker', LogLevel.Info);
    minLevels.set('AudioPlayerController', LogLevel.Info);
    minLevels.set('AudioPlayer', LogLevel.Info);
    minLevels.set('UserActivityUI', LogLevel.Info);
    minLevels.set('VirtualList', LogLevel.Info);
    minLevels.set('MenuHost', LogLevel.Info);

    // Bumping down levels of in-dev scopes
    minLevels.set('AudioContextLazy', LogLevel.Debug);
    minLevels.set('AudioRecorder', LogLevel.Debug);
    minLevels.set('Landing', LogLevel.Debug);
    // minLevels.set('TuneUI', LogLevel.Debug);
    // minLevels.set('MenuHost', LogLevel.Debug);
    // minLevels.set('HistoryUI', LogLevel.Debug);

    // minLevels.clear(); // To quickly discard any tweaks :)
    persist(minLevels);
}
