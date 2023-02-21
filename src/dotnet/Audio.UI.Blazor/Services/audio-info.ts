import { PromiseSource } from 'promises';
import { Log, LogLevel, LogScope } from 'logging';
import { opusMediaRecorder } from '../Components/AudioRecorder/opus-media-recorder';

const LogScope: LogScope = 'AudioInfo';
const log = Log.get(LogScope, LogLevel.Info);
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);


export class AudioInfo {
    private static backendRef: DotNet.DotNetObject = null;

    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static async init(backendRef1: DotNet.DotNetObject, baseUri: string): Promise<void> {
        this.backendRef = backendRef1;
        log?.log(`init`);

        await opusMediaRecorder.load(baseUri);
        this.whenReady.resolve(undefined);
        globalThis["audioInfo"] = this;
        log?.log(`ready`);
    }
}

