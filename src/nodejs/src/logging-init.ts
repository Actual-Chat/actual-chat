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

    public reset(mustPersist = true) {
        reset(this.minLevels);
        if (mustPersist)
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
    if (!readMinLevels.size || readMinLevels.size == 0)
        return false;

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
    minLevels.set('VirtualList', LogLevel.Info);
    minLevels.set('MenuHost', LogLevel.Info);

    // Bumping down levels of in-dev scopes
    minLevels.set('AudioContextLazy', LogLevel.Debug);
    minLevels.set('AudioRecorder', LogLevel.Debug);
    // minLevels.set('MenuHost', LogLevel.Debug);

    // minLevels.clear(); // To quickly discard any tweaks :)
    persist(minLevels);
}
