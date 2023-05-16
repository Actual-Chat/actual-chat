import { Log } from 'logging';

const { debugLog, warnLog } = Log.get('AppSettings');

export class AppSettings {
    public static init(baseUri: string, sessionHash: string): void {
        const app = globalThis?.App as unknown;
        if (!app) {
            warnLog?.log('init: globalThis.App is undefined');
            return;
        }

        app['baseUri'] = baseUri;
        app['sessionHash'] = sessionHash;
        debugLog?.log('init: baseUri:', baseUri, ', sessionHash:', sessionHash);
    }
}
