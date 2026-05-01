// Single source of truth for app-wide TypeScript logging.
//
// Re-exports the underlying API from @actuallab/core (synced into
// ./actuallab-core/) and adds the app's typed scope union plus per-scope
// defaults.  Use getLogs() at call sites:
//
//   import { getLogs } from 'logging';
//   const { debugLog, warnLog } = getLogs('AudioPlayer');
//
// Runtime overrides are still available via the dev-console controller:
//   logLevels.override('AudioPlayer', 1)        // Debug
//   logLevels.overrideAll('Audio', 1)           // every scope starting with 'Audio'
//   logLevels.dump()                            // table of every known scope
//   logLevels.reset()                           // back to package defaults
//
// To force a scope to Debug for development, change its entry in `defaults`
// below — that's the package-default; user overrides via logLevels.override
// take precedence and persist in sessionStorage.

import {
    createLogProvider,
    Log,
    LogLevel,
} from 'actuallab-core';

export { Log, LogLevel } from './actuallab-core/index.js';
export type { LogRef, LogScopeFns } from './actuallab-core/index.js';

// All known app-level log scopes.  No prefix — scopes are flat strings.
// (The 'rpc.' prefix used by @actuallab/rpc keeps its scopes from clashing
// with anything declared here.)
export type LogScope =
    | 'default'
    // Library
    | 'Api'
    | 'AsyncProcessor'
    | 'BrowserInfo'
    | 'BrowserInit'
    | 'BubbleHost'
    | 'ConnectivityUI'
    | 'DelayedInvoker'
    | 'FileUpload'
    | 'EmojiPreview'
    | 'EventHandling'
    | 'Gestures'
    | 'InertialScroll'
    | 'NoSleep'
    | 'History'
    | 'Interactive'
    | 'Kvas'
    | 'KvasBackend'
    | 'MainThreadDiagnostics'
    | 'MenuHost'
    | 'ModalHost'
    | 'OnDeviceAwake'
    | 'promises'
    | 'ResilientStream'
    | 'Rpc'
    | 'RecaptchaHandler'
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
    | 'AudioContextTraits'
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
    | 'AudioStreamer'
    | 'OpusMediaRecorder'
    | 'AudioVadWorker'
    | 'AudioVadWorkletProcessor'
    | 'OpusEncoderWorkletProcessor'
    | 'OpusEncoderWorker'
    | 'WarmUpAudioWorkletProcessor'
    | 'WebRtcAec'
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
    | 'VideoPanel'
    | 'AudioVideoSync'
    | 'JoinVideoCallModal'
    | 'VideoPlayer'
    | 'VideoRecorder'
    | 'VideoStreamingPreview'
    | 'VideoPipeline'
    | 'VideoEncoder'
    | 'VideoDecoder'
    | 'VideoSegmentation'
    | 'BlurPreviewSession'
    | 'CameraPermission'
    | 'VisualMediaViewer'
    | 'WebAuth'
    | 'WebFileProvider';

// Per-scope defaults.  Most are Warn (quiet by default); video scopes are
// Debug for ongoing latency troubleshooting (preserved from the previous
// logging-init.ts reset() block).
const defaults: Record<LogScope, LogLevel> = {
    default: LogLevel.Warn,
    // Library
    Api: LogLevel.Warn,
    AsyncProcessor: LogLevel.Warn,
    BrowserInfo: LogLevel.Warn,
    BrowserInit: LogLevel.Warn,
    BubbleHost: LogLevel.Warn,
    ConnectivityUI: LogLevel.Warn,
    DelayedInvoker: LogLevel.Warn,
    FileUpload: LogLevel.Warn,
    EmojiPreview: LogLevel.Warn,
    EventHandling: LogLevel.Warn,
    Gestures: LogLevel.Warn,
    InertialScroll: LogLevel.Warn,
    NoSleep: LogLevel.Warn,
    History: LogLevel.Warn,
    Interactive: LogLevel.Warn,
    Kvas: LogLevel.Warn,
    KvasBackend: LogLevel.Warn,
    MainThreadDiagnostics: LogLevel.Info,
    MenuHost: LogLevel.Warn,
    ModalHost: LogLevel.Warn,
    OnDeviceAwake: LogLevel.Warn,
    promises: LogLevel.Warn,
    ResilientStream: LogLevel.Warn,
    Rpc: LogLevel.Warn,
    RecaptchaHandler: LogLevel.Warn,
    ScreenSize: LogLevel.Warn,
    ServiceWorker: LogLevel.Warn,
    SessionTokens: LogLevel.Warn,
    TimerQueue: LogLevel.Warn,
    UndoStack: LogLevel.Warn,
    Versioning: LogLevel.Warn,
    VirtualList: LogLevel.Warn,
    // XxxUI
    DebugUI: LogLevel.Warn,
    DeviceAwakeUI: LogLevel.Warn,
    FocusUI: LogLevel.Warn,
    InteractiveUI: LogLevel.Warn,
    KeepAwakeUI: LogLevel.Warn,
    LanguageUI: LogLevel.Warn,
    NotificationUI: LogLevel.Warn,
    TuneUI: LogLevel.Warn,
    UserActivityUI: LogLevel.Warn,
    VibrationUI: LogLevel.Warn,
    Share: LogLevel.Warn,
    // Audio
    AudioContextRef: LogLevel.Warn,
    AudioContextSource: LogLevel.Warn,
    AudioContextTraits: LogLevel.Warn,
    AudioInfo: LogLevel.Warn,
    // Audio playback
    AudioPlayer: LogLevel.Warn,
    FallbackPlayback: LogLevel.Warn,
    OpusDecoder: LogLevel.Warn,
    OpusDecoderWorker: LogLevel.Warn,
    FeederNode: LogLevel.Warn,
    FeederProcessor: LogLevel.Warn,
    SoundsPlayer: LogLevel.Warn,
    // Audio recording
    AudioRecorder: LogLevel.Warn,
    AudioStreamer: LogLevel.Warn,
    OpusMediaRecorder: LogLevel.Warn,
    AudioVadWorker: LogLevel.Warn,
    AudioVadWorkletProcessor: LogLevel.Warn,
    OpusEncoderWorkletProcessor: LogLevel.Warn,
    OpusEncoderWorker: LogLevel.Warn,
    WarmUpAudioWorkletProcessor: LogLevel.Warn,
    WebRtcAec: LogLevel.Warn,
    // Isolated components
    Attachments: LogLevel.Warn,
    ChatMessageEditor: LogLevel.Warn,
    CodeBlockMarkupView: LogLevel.Warn,
    CopyTrigger: LogLevel.Warn,
    FontSizes: LogLevel.Warn,
    Landing: LogLevel.Warn,
    LandingLeftMenu: LogLevel.Warn,
    MarkupEditor: LogLevel.Warn,
    MessageEditor: LogLevel.Warn,
    SearchPanel: LogLevel.Warn,
    SideNav: LogLevel.Warn,
    SelectionHost: LogLevel.Warn,
    TextBox: LogLevel.Warn,
    Theme: LogLevel.Warn,
    TooltipHost: LogLevel.Warn,
    UserInterface: LogLevel.Warn,
    // Video — Debug for latency troubleshooting (preserved from old reset())
    VideoPanel: LogLevel.Debug,
    AudioVideoSync: LogLevel.Debug,
    JoinVideoCallModal: LogLevel.Info,
    VideoPlayer: LogLevel.Debug,
    VideoRecorder: LogLevel.Warn,
    VideoStreamingPreview: LogLevel.Warn,
    VideoPipeline: LogLevel.Debug,
    VideoEncoder: LogLevel.Warn,
    VideoDecoder: LogLevel.Warn,
    VideoSegmentation: LogLevel.Warn,
    BlurPreviewSession: LogLevel.Warn,
    CameraPermission: LogLevel.Warn,
    VisualMediaViewer: LogLevel.Warn,
    WebAuth: LogLevel.Warn,
    WebFileProvider: LogLevel.Warn,
};

export const getLogs = createLogProvider<LogScope>('', defaults);

// `Log` is referenced statically, so make sure logging is initialized.
void Log.get('default');
