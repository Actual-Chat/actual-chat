// Runs libavjs-webcodecs-polyfill on a SIMD libav.js build in place of the
// browser's WebCodecs, at one of three levels: `none` leaves the browser alone;
// `vp9` replaces only the VP9 encoder, for Firefox, whose own needs ~60 ms per
// 720p frame against a 33 ms budget for the whole ladder where this build needs
// ~6 ms; `full` replaces every WebCodecs class, for engines that ship none.
// The level is resolved once on the main thread and pushed to workers through
// SharedSettings. At `none` the wasm is never fetched.

import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';

const { debugLog, errorLog, infoLog, warnLog } = getLogs('WebCodecsPolyfill');

const LIBAV_FILE = 'libav-6.10.9.0-vp9-opus-avf-simd.js';
const POLYFILL_FILE = 'libavjs-webcodecs-polyfill.js';

const POLYFILL_CLASS_NAMES = [
    'EncodedAudioChunk', 'AudioData', 'AudioDecoder', 'AudioEncoder',
    'EncodedVideoChunk', 'VideoFrame', 'VideoDecoder', 'VideoEncoder',
] as const;

export type WebCodecsPolyfillLevel = 'none' | 'vp9' | 'full';
export type WebCodecsPolyfillOverride = 'auto' | WebCodecsPolyfillLevel;

export interface WebCodecsPolyfillConfig {
    level: WebCodecsPolyfillLevel;
    // Resolved on the main thread through Versioning: the import map that drives
    // it is not available in workers.
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

interface CodecClass {
    isConfigSupported?: (config: unknown) => Promise<unknown>;
}

export const WEB_CODECS_POLYFILL_LEVELS: readonly WebCodecsPolyfillLevel[] = ['none', 'vp9', 'full'];

let appliedLevel: WebCodecsPolyfillLevel = 'none';
let applying: Promise<WebCodecsPolyfillLevel> | null = null;
let classes: WebCodecsPolyfillClasses | null = null;

export function getWebCodecsPolyfillLevel(): WebCodecsPolyfillLevel {
    return appliedLevel;
}

export function getWebCodecsPolyfillClasses(): WebCodecsPolyfillClasses | null {
    return classes;
}

export function isWebCodecsPolyfillLevel(value: unknown): value is WebCodecsPolyfillLevel {
    return typeof value === 'string' && (WEB_CODECS_POLYFILL_LEVELS as readonly string[]).includes(value);
}

// `full` only where there is no WebCodecs at all, so the fallback never displaces
// a working native implementation except on Firefox's measured VP9 encoder.
export function resolveWebCodecsPolyfillLevel(
    override: WebCodecsPolyfillOverride,
): WebCodecsPolyfillLevel {
    if (override !== 'auto')
        return override;
    if (typeof (globalThis as { VideoEncoder?: unknown }).VideoEncoder === 'undefined')
        return 'full';

    return DeviceInfo.isFirefox ? 'vp9' : 'none';
}

// Idempotent, and one-way per realm: scripts cannot be unloaded and `full`'s
// global replacement cannot be undone once encoders exist, so switching needs
// a reload.
export function applyWebCodecsPolyfill(config: WebCodecsPolyfillConfig): Promise<WebCodecsPolyfillLevel> {
    if (applying !== null) {
        if (config.level !== appliedLevel)
            warnLog?.log(`applyWebCodecsPolyfill: at '${appliedLevel}', ignoring '${config.level}'`);

        return applying;
    }

    if (config.level === 'none') {
        appliedLevel = 'none';
        applying = Promise.resolve('none' as WebCodecsPolyfillLevel);

        return applying;
    }

    applying = applyImpl(config).catch((error: unknown) => {
        // Staying native is worse than the polyfill but better than no video;
        // callers that cannot live with it re-check getWebCodecsPolyfillLevel.
        errorLog?.log(`applyWebCodecsPolyfill('${config.level}') failed, staying native:`, error);
        appliedLevel = 'none';

        return 'none' as WebCodecsPolyfillLevel;
    });

    return applying;
}

export function whenWebCodecsPolyfillReady(): Promise<WebCodecsPolyfillLevel> {
    return applying ?? Promise.resolve('none' as WebCodecsPolyfillLevel);
}

// Private methods

// In a worker importScripts blocks, so the classes are installed before this
// returns and no caller can observe a realm that has the level but not the
// globals. Only wasm init is deferred, behind isConfigSupported.
function applyImpl(config: WebCodecsPolyfillConfig): Promise<WebCodecsPolyfillLevel> {
    const startedAt = performance.now();
    const base = config.baseUrl.replace(/\/$/, '');
    // Setting globalThis.LibAV is the whole mechanism for pointing the polyfill
    // at this build instead of the CDN one it would otherwise fetch.
    (globalThis as { LibAV?: unknown }).LibAV = { base, nothreads: true };
    const scope = globalThis as { importScripts?: (url: string) => void };
    if (typeof scope.importScripts === 'function') {
        scope.importScripts(`${base}/${LIBAV_FILE}`);
        scope.importScripts(`${base}/${POLYFILL_FILE}`);

        return install(config, startedAt);
    }

    return loadScript(`${base}/${LIBAV_FILE}`)
        .then(() => loadScript(`${base}/${POLYFILL_FILE}`))
        .then(() => install(config, startedAt));
}

function install(config: WebCodecsPolyfillConfig, startedAt: number): Promise<WebCodecsPolyfillLevel> {
    const loaded = (globalThis as { LibAVWebCodecs?: WebCodecsPolyfillClasses }).LibAVWebCodecs;
    if (!loaded)
        throw new Error(`${POLYFILL_FILE} loaded but defined no LibAVWebCodecs`);

    classes = loaded;
    appliedLevel = config.level;
    // Ponyfill mode: the polyfill's own installer fills in MISSING classes only.
    const whenLoaded = loaded.load({ polyfill: false, libavOptions: { nothreads: true } });
    if (config.level === 'full')
        installGlobals(loaded, whenLoaded);

    return whenLoaded.then(() => {
        infoLog?.log(
            `applyWebCodecsPolyfill: '${config.level}' ready in ${Math.round(performance.now() - startedAt)}ms`);

        return config.level;
    });
}

// Overwrites unconditionally, unlike the polyfill's own installer: the realms
// cannot be mixed, so at `full` everything must come from the same one.
function installGlobals(loaded: WebCodecsPolyfillClasses, whenLoaded: Promise<void>): void {
    const target = globalThis as unknown as Record<string, unknown>;
    const source = loaded as unknown as Record<string, unknown>;
    for (const name of POLYFILL_CLASS_NAMES) {
        const value = source[name];
        if (!value) {
            warnLog?.log(`installGlobals: polyfill exports no ${name}`);
            continue;
        }

        target[name] = withDeferredConfigSupport(value as CodecClass, whenLoaded);
    }

    debugLog?.log(`installGlobals: replaced ${POLYFILL_CLASS_NAMES.join(', ')}`);
}

// Classes work as soon as the script evaluates, but isConfigSupported reads
// capability lists libav fills in later, reporting "unsupported" until then.
function withDeferredConfigSupport(codecClass: CodecClass, whenLoaded: Promise<void>): CodecClass {
    const original = codecClass.isConfigSupported;
    if (typeof original !== 'function')
        return codecClass;

    const isConfigSupported = original.bind(codecClass) as (config: unknown) => Promise<unknown>;
    codecClass.isConfigSupported = async (config: unknown): Promise<unknown> => {
        await whenLoaded;

        return isConfigSupported(config);
    };

    return codecClass;
}

function loadScript(url: string): Promise<void> {
    const scope = globalThis as { importScripts?: (url: string) => void; document?: Document };
    if (typeof scope.importScripts === 'function') {
        scope.importScripts(url);

        return Promise.resolve();
    }

    const doc = scope.document;
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
