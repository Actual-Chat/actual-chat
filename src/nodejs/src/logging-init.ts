// Should be the same as logging.LogLevel, but local to this module
enum LogLevel {
    Debug = 1,
    Info,
    Warn,
    Error,
    None = 1000,
}

export function initLogging(Log: unknown) : void {
    Log['defaultMinLevel'] = LogLevel.Info;
    const minLevels = Log['minLevels'] as Map<string, LogLevel>;

    minLevels.set('AudioContextLazy', LogLevel.Debug);
    minLevels.set('NextInteraction', LogLevel.Debug);
    minLevels.set('on-device-awake', LogLevel.Debug);
    minLevels.set('Rpc', LogLevel.Debug);
}
