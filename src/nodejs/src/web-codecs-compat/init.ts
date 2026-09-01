// One entry point for every WebCodecs difference between engines. `level`
// says which implementation this realm runs on, `whenReady` gates anything
// that may depend on it, and both are no-ops at level `none`, which is what
// every browser but Firefox and the WebCodecs-less WebViews resolves to.
//
//   none  the browser's own WebCodecs, untouched; nothing is fetched.
//   vp9   the VP9 encoder only. Firefox's needs ~60 ms per 720p frame against
//         a 33 ms budget for the whole ladder; this build sustains ~6 ms.
//         Everything else - AV1, HEVC, H.264, decode - stays native.
//   full  every WebCodecs class, audio included, for engines that ship none.
//
// Install is synchronous wherever it can be: in a worker importScripts blocks,
// so the classes are in place before init() returns and no caller can observe
// a realm that has the level but not the globals. Only the wasm init is
// deferred, and that is what `whenReady` is for.

import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';

const { debugLog, errorLog, infoLog, warnLog } = getLogs('WebCodecsCompat');

const LIBAV_FILE = 'libav-6.10.9.0-vp9-opus-avf-simd.js';
const POLYFILL_FILE = 'libavjs-webcodecs-polyfill.js';

const POLYFILL_CLASS_NAMES = [
    'EncodedAudioChunk', 'AudioData', 'AudioDecoder', 'AudioEncoder',
    'EncodedVideoChunk', 'VideoFrame', 'VideoDecoder', 'VideoEncoder',
] as const;

export type WebCodecsLevel = 'none' | 'vp9' | 'full';
export type WebCodecsLevelOverride = 'auto' | WebCodecsLevel;

export const WEB_CODECS_LEVELS: readonly WebCodecsLevel[] = ['none', 'vp9', 'full'];

export interface WebCodecsCompatConfig {
    level: WebCodecsLevel;
    // Resolved on the main thread: workers have no import map to map it from.
    baseUrl: string;
}

export interface WebCodecsPolyfillClasses {
    VideoEncoder: typeof VideoEncoder;
    VideoDecoder: typeof VideoDecoder;
    VideoFrame: typeof VideoFrame;
    EncodedVideoChunk: typeof EncodedVideoChunk;
    AudioEncoder: unknown;
    AudioDecoder: unknown;
    AudioData: unknown;
    EncodedAudioChunk: unknown;
    load: (options: { polyfill?: boolean; libavOptions?: unknown }) => Promise<void>;
}

export function isWebCodecsLevel(value: unknown): value is WebCodecsLevel {
    return typeof value === 'string' && (WEB_CODECS_LEVELS as readonly string[]).includes(value);
}

const READY: Promise<void> = Promise.resolve();

export class WebCodecsCompat {
    private static _level: WebCodecsLevel = 'none';
    private static _isReady = true;
    private static _whenReady: Promise<void> = READY;
    private static _classes: WebCodecsPolyfillClasses | null = null;

    static get level(): WebCodecsLevel {
        return this._level;
    }

    /** True when awaiting {@link whenReady} would not actually wait, so a hot path
     *  can skip the microtask entirely. */
    static get isReady(): boolean {
        return this._isReady;
    }

    /** Gate for anything that may touch a polyfilled class. Already resolved at
     *  level `none`, and at any level once the wasm is up. */
    static get whenReady(): Promise<void> {
        return this._whenReady;
    }

    static get classes(): WebCodecsPolyfillClasses | null {
        return this._classes;
    }

    /** `full` only where there is no WebCodecs at all, so the fallback never displaces
     *  a working native implementation except on Firefox's VP9 encoder. */
    static resolveLevel(override: WebCodecsLevelOverride): WebCodecsLevel {
        if (override !== 'auto')
            return override;
        if (typeof (globalThis as { VideoEncoder?: unknown }).VideoEncoder === 'undefined')
            return 'full';

        return DeviceInfo.isFirefox ? 'vp9' : 'none';
    }

    /** Idempotent, and one-way per realm: `full`'s global replacement cannot be undone
     *  once codecs exist, so switching levels needs a reload. */
    static init(config: WebCodecsCompatConfig): Promise<void> {
        if (this._whenReady !== READY) {
            if (config.level !== this._level)
                warnLog?.log(`init: at '${this._level}', ignoring '${config.level}'`);

            return this._whenReady;
        }

        if (config.level === 'none')
            return this._whenReady;

        this._isReady = false;
        this._whenReady = this.install(config).catch((error: unknown) => {
            // Staying native is worse than the polyfill but better than no media;
            // callers see it through `level`, which stays 'none'.
            errorLog?.log(`init('${config.level}') failed, staying native:`, error);
            this._level = 'none';
        }).finally(() => {
            this._isReady = true;
        });

        return this._whenReady;
    }

    // Private methods

    private static async install(config: WebCodecsCompatConfig): Promise<void> {
        const startedAt = performance.now();
        const base = config.baseUrl.replace(/\/$/, '');
        // Setting globalThis.LibAV is the whole mechanism for pointing the
        // polyfill at this build instead of the CDN one it would fetch.
        (globalThis as { LibAV?: unknown }).LibAV = { base, nothreads: true };
        const scope = globalThis as { importScripts?: (url: string) => void };
        if (typeof scope.importScripts === 'function') {
            scope.importScripts(`${base}/${LIBAV_FILE}`);
            scope.importScripts(`${base}/${POLYFILL_FILE}`);
        }
        else {
            await loadScript(`${base}/${LIBAV_FILE}`);
            await loadScript(`${base}/${POLYFILL_FILE}`);
        }

        const loaded = (globalThis as { LibAVWebCodecs?: WebCodecsPolyfillClasses }).LibAVWebCodecs;
        if (!loaded)
            throw new Error(`${POLYFILL_FILE} loaded but defined no LibAVWebCodecs`);

        this._classes = loaded;
        this._level = config.level;
        if (config.level === 'full')
            installGlobals(loaded);

        // Ponyfill mode: the polyfill's own installer fills in MISSING classes only.
        // This populates the capability lists behind isConfigSupported, which
        // report everything unsupported until it resolves.
        await loaded.load({ polyfill: false, libavOptions: { nothreads: true } });
        infoLog?.log(`init: '${config.level}' ready in ${Math.round(performance.now() - startedAt)}ms`);
    }
}

// Private methods

// Overwrites unconditionally, unlike the polyfill's own installer: the two
// realms cannot be mixed, so at `full` everything must come from the same one.
function installGlobals(loaded: WebCodecsPolyfillClasses): void {
    const target = globalThis as unknown as Record<string, unknown>;
    const source = loaded as unknown as Record<string, unknown>;
    for (const name of POLYFILL_CLASS_NAMES) {
        if (source[name])
            target[name] = source[name];
        else
            warnLog?.log(`installGlobals: polyfill exports no ${name}`);
    }

    debugLog?.log(`installGlobals: replaced ${POLYFILL_CLASS_NAMES.join(', ')}`);
}

function loadScript(url: string): Promise<void> {
    const doc = (globalThis as { document?: Document }).document;
    if (!doc)
        return Promise.reject(new Error(`Cannot load ${url}: no importScripts and no document`));

    return new Promise<void>((resolve, reject) => {
        const script = doc.createElement('script');
        script.src = url;
        script.async = true;
        script.onload = () => resolve();
        script.onerror = () => reject(new Error(`Failed to load ${url}`));
        doc.head.appendChild(script);
    });
}
