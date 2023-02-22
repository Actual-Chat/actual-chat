// Should be the same as logging.LogLevel, but local to this module
export enum LogLevel {
    Debug = 1,
    Info,
    Warn,
    Error,
    None = 1000,
}

export type LogScope =
    'default'
    // Library
    | 'BrowserInfo'
    | 'Clipboard'
    | 'Gestures'
    | 'event-handling'
    | 'History'
    | 'Interactive'
    | 'LocalSettings'
    | 'MenuHost'
    | 'ModalHost'
    | 'OnDeviceAwake'
    | 'OnDeviceAwakeWorker'
    | 'promises'
    | 'Rpc'
    | 'UndoStack'
    | 'VirtualList'
    // XxxUI
    | 'DebugUI'
    | 'FocusUI'
    | 'InteractiveUI'
    | 'KeepAwakeUI'
    | 'NotificationUI'
    | 'TuneUI'
    | 'UserActivityUI'
    | 'VibrationUI'
    // Audio
    | 'AudioContextSource'
    | 'AudioContextRef'
    | 'ChromiumEchoCancellation'
    | 'WarmUpAudioWorkletProcessor'
    | 'FeederProcessor'
    | 'FeederNode'
    | 'OpusEncoderWorker'
    | 'OpusEncoderWorkletProcessor'
    | 'OpusDecoder'
    | 'OpusDecoderWorker'
    | 'AudioPlayerController'
    | 'AudioPlayer'
    | 'AudioVad'
    | 'AudioRecorder'
    // Isolated components
    | 'ChatMessageEditor'
    | 'CopyTrigger'
    | 'ImageViewer'
    | 'Landing'
    | 'LandingLeftMenu'
    | 'MarkupEditor'
    | 'PicUpload'
    | 'SideNav'
    | string;

const GlobalThisKey = 'logLevels';
const StorageKey = 'logLevels';

export function initLogging(Log: unknown): void {
    Log['defaultMinLevel'] = LogLevel.Info;
    const minLevels = Log['minLevels'] as Map<LogScope, LogLevel>;

    let wasRestored = false;
    if (globalThis) {
        globalThis[GlobalThisKey] = new LogLevelController(minLevels);
        wasRestored = restore(minLevels);
    }
    if (!wasRestored)
        reset(minLevels);
}

class LogLevelController {
    constructor (private minLevels: Map<LogScope, LogLevel>)
    { }

    public override(scope: LogScope, newLevel: LogLevel): void {
        this.minLevels.set(scope, newLevel);
        persist(this.minLevels);
    }

    public reset() {
        reset(this.minLevels);
        persist(this.minLevels);
    }

    public clear(defaultLevel?: LogLevel) {
        this.minLevels.clear();
        if (defaultLevel !== undefined)
            this.minLevels['default'] = defaultLevel;
        persist(this.minLevels);
    }
}

function restore(minLevels: Map<string, LogLevel>): boolean {
    const storage = globalThis?.sessionStorage;
    if (!storage)
        return false;

    const readJson = storage.getItem(StorageKey);
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

    storage.setItem(StorageKey, JSON.stringify(Array.from(minLevels.entries())));
    return true;
}

function reset(minLevels: Map<LogScope, LogLevel>): void {
    minLevels.clear();

    // Bumping down levels of in-dev scopes
    // minLevels.set('Gestures', LogLevel.Debug);
    // minLevels.set('event-handling', LogLevel.Debug);
    minLevels.set('Interactive', LogLevel.Debug);
    minLevels.set('OnDeviceAwake', LogLevel.Debug);
    minLevels.set('AudioPlayerController', LogLevel.Debug);
    minLevels.set('AudioContextSource', LogLevel.Debug);
    // minLevels.set('AudioContextRef', LogLevel.Debug);
    minLevels.set('AudioRecorder', LogLevel.Debug);
    minLevels.set('OpusMediaRecorder', LogLevel.Debug);
    minLevels.set('AudioVadWorker', LogLevel.Debug);
    minLevels.set('OpusEncoderWorker', LogLevel.Debug);
    // minLevels.set('History', LogLevel.Debug);
    // minLevels.set('MenuHost', LogLevel.Debug);
    // minLevels.set('MarkupEditor', LogLevel.Debug);
    // minLevels.set('ChatMessageEditor', LogLevel.Debug);
    minLevels.set('Landing', LogLevel.Debug);
    minLevels.set('LandingLeftMenu', LogLevel.Debug);

    // XxxUI
    // minLevels.set('FocusUI', LogLevel.Debug);
    // minLevels.set('KeepAwakeUI', LogLevel.Debug);
    minLevels.set('NotificationUI', LogLevel.Debug);
    // minLevels.set('TuneUI', LogLevel.Debug);

    // minLevels.clear(); // To quickly discard any tweaks :)
    persist(minLevels);
}
