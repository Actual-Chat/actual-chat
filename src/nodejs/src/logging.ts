import { Log, LogLevel } from 'logging-abstractions';
export { Log, LogLevel };
import { ConsoleLog } from 'logging-console';

Log.loggerFactory = (scope, level) => new ConsoleLog(scope, level);
Log.defaultMinLevel = LogLevel.Info;

Log.minLevels.set('AudioContextLazy', LogLevel.Debug);
Log.minLevels.set('NextInteraction', LogLevel.Debug);
Log.minLevels.set('on-device-awake', LogLevel.Debug);
Log.minLevels.set('Rpc', LogLevel.Debug);
