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
    | 'AsyncProcessor'
    | 'BrowserInfo'
    | 'BrowserInit'
    | 'BubbleHost'
    | 'Connectivity'
    | 'Gestures'
    | 'event-handling'
    | 'InertialScroll'
    | 'NoSleep'
    | 'History'
    | 'Interactive'
    | 'Kvas'
    | 'KvasBackend'
    | 'MenuHost'
    | 'ModalHost'
    | 'OnDeviceAwake'
    | 'promises'
    | 'Rpc'
    | 'ScreenSize'
    | 'ServiceWorker'
    | 'SessionTokens'
    | 'TimerQueue'
    | 'UndoStack'
    | 'Versioning'
    | 'VirtualList'
    // XxxUI
    | 'DebugUI'
    | 'DeviceAwakeUI'
    | 'FocusUI'
    | 'InteractiveUI'
    | 'KeepAwakeUI'
    | 'LanguageUI'
    | 'NotificationUI'
    | 'TuneUI'
    | 'UserActivityUI'
    | 'VibrationUI'
    | 'Share'
    // Audio
    | 'AudioContextRef'
    | 'AudioContextSource'
    | 'AudioInfo'
    // Audio playback
    | 'AudioPlayer'
    | 'FallbackPlayback'
    | 'OpusDecoder'
    | 'OpusDecoderWorker'
    | 'FeederNode'
    | 'FeederProcessor'
    | 'SoundsPlayer'
    // Audio recording
    | 'AudioRecorder'
    | 'OpusMediaRecorder'
    | 'AudioVadWorker'
    | 'AudioVadWorkletProcessor'
    | 'OpusEncoderWorkletProcessor'
    | 'OpusEncoderWorker'
    | 'WarmUpAudioWorkletProcessor'
    | 'WebRtcAec' // Unused
    // Isolated components
    | 'Attachments'
    | 'ChatMessageEditor'
    | 'CodeBlockMarkupView'
    | 'CopyTrigger'
    | 'FontSizes'
    | 'Landing'
    | 'LandingLeftMenu'
    | 'MarkupEditor'
    | 'MessageEditor'
    | 'SearchPanel'
    | 'SideNav'
    | 'SelectionHost'
    | 'TextBox'
    | 'Theme'
    | 'TooltipHost'
    | 'UserInterface'
    | 'VisualMediaViewer';

const GlobalThisKey = 'logLevels';
const StorageKey = 'logLevels';
const DateStorageKey = `${StorageKey}.date`;
const MaxStorageAge = 86_400_000 * 3; // 3 days

const app = globalThis?.['App'] as unknown;
const isWorkerOrWorklet = !app;

export function initLogging(Log: unknown): void {
    Log['defaultMinLevel'] = LogLevel.Info;
    const minLevels = Log['minLevels'] as Map<LogScope, LogLevel>;

    let wasRestored = false;
    if (globalThis && !isWorkerOrWorklet) {
        globalThis[GlobalThisKey] = new LogLevelController(minLevels);
        wasRestored = restore(minLevels);
    }
    if (wasRestored) {
        console.log(`Logging: logLevels are restored`);
    }
    else {
        if (!isWorkerOrWorklet)
            console.log(`Logging: logLevels are reset`);
        reset(minLevels);
    }
}

class LogLevelController {
    constructor (private minLevels: Map<LogScope, LogLevel>)
    { }

    public override(scope: LogScope, newLevel: LogLevel): void {
        this.minLevels.set(scope, newLevel);
        persist(this.minLevels);
    }

    public reset(isProduction?: boolean) {
        reset(this.minLevels, isProduction);
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

    const dateJson = storage.getItem(DateStorageKey);
    if (!dateJson)
        return false;
    if (Date.now() - JSON.parse(dateJson) > MaxStorageAge)
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

    storage.setItem(DateStorageKey, JSON.stringify(Date.now()));
    storage.setItem(StorageKey, JSON.stringify(Array.from(minLevels.entries())));
    return true;
}

function reset(minLevels: Map<LogScope, LogLevel>, isProduction?: boolean): void {
    minLevels.clear();
    // enabled debug logging temporarily - do not add chatty log scopes!! - 14.11.2023 AK

    // isProduction ??= app?.['environment'] == 'Production';
    // if (isProduction)
    //     return;

    // Bumping down levels of in-dev scopes
    // minLevels.set('Versioning', LogLevel.Debug);
    // minLevels.set('Gestures', LogLevel.Debug);
    // minLevels.set('event-handling', LogLevel.Debug);
    // minLevels.set('Rpc', LogLevel.Debug);
    // minLevels.set('AsyncProcessor', LogLevel.Debug);
    // minLevels.set('promises', LogLevel.Debug);
    minLevels.set('Interactive', LogLevel.Debug);
    minLevels.set('OnDeviceAwake', LogLevel.Debug);
    minLevels.set('AudioContextRef', LogLevel.Debug);
    minLevels.set('AudioContextSource', LogLevel.Debug);
    minLevels.set('AudioPlayer', LogLevel.Debug);
    minLevels.set('FallbackPlayback', LogLevel.Debug);
    // minLevels.set('OpusDecoder', LogLevel.Debug);
    // minLevels.set('OpusDecoderWorker', LogLevel.Debug);
    // minLevels.set('FeederProcessor', LogLevel.Debug);
    minLevels.set('AudioRecorder', LogLevel.Debug);
    minLevels.set('OpusMediaRecorder', LogLevel.Debug);
    minLevels.set('AudioVadWorker', LogLevel.Debug);
    // minLevels.set('AudioVadWorkletProcessor', LogLevel.Debug);
    minLevels.set('OpusEncoderWorker', LogLevel.Debug);
    // minLevels.set('OpusEncoderWorkletProcessor', LogLevel.Debug);
    // minLevels.set('InertialScroll', LogLevel.Debug);
    minLevels.set('VirtualList', LogLevel.Debug);
    // minLevels.set('Landing', LogLevel.Debug);
    // minLevels.set('LandingLeftMenu', LogLevel.Debug);

    // XxxUI
    // minLevels.set('FocusUI', LogLevel.Debug);
    // minLevels.set('KeepAwakeUI', LogLevel.Debug);
    // minLevels.set('NoSleep', LogLevel.Debug);
    // minLevels.set('NotificationUI', LogLevel.Debug);
    // minLevels.set('TuneUI', LogLevel.Debug);
    // minLevels.set('SoundsPlayer', LogLevel.Debug);

    // Isolated components
    // minLevels.set('History', LogLevel.Debug);
    // minLevels.set('MenuHost', LogLevel.Debug);
    // minLevels.set('MarkupEditor', LogLevel.Debug);
    // minLevels.set('ChatMessageEditor', LogLevel.Debug);

    // minLevels.clear(); // To quickly discard any tweaks :)
    persist(minLevels);
}
