import { PromiseSource } from 'promises';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'History';
const log = Log.get(LogScope, LogLevel.Info);
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export class History {
    private static backendRef: DotNet.DotNetObject = null;
    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static init(backendRef1: DotNet.DotNetObject): void {
        this.backendRef = backendRef1;
        this.whenReady.resolve(undefined);
        globalThis["App"]["history"] = this;
    }

    public static async navigateTo(uri: string, mustAddHistoryItem: boolean = false): Promise<void> {
        log?.log(`navigateTo:`, uri);
        await this.whenReady;
        await this.backendRef.invokeMethodAsync('NavigateTo', uri, mustAddHistoryItem);
    };
}
