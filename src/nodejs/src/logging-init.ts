// Should be the same as logging.LogLevel, but local to this module
enum LogLevel {
    Debug = 1,
    Info,
    Warn,
    Error,
    None = 1000,
}

export function initLogging(Log: unknown) : void {
    Log['defaultMinLevel'] = LogLevel.Debug;
    const minLevels = Log['minLevels'] as Map<string, LogLevel>;

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
    minLevels.set('VirtualList', LogLevel.Debug);
    minLevels.set('VirtualListPlan', LogLevel.Debug);

    // minLevels.clear(); // To quickly discard any tweaks :)
}
