import { Log } from 'logging';

const { debugLog } = Log.get('AppSettings');

export class AppSettings {
    public static init(baseUri: string, sessionHash: string): void {
        const app = window['App'] as unknown;
        app['baseUri'] = baseUri;
        app['sessionHash'] = sessionHash;
        debugLog?.log('init: baseUri:', baseUri, ', sessionHash:', sessionHash);
    }
}
