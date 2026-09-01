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
// Loading is asynchronous everywhere: this app's workers are ES modules, which
// have no importScripts, so libav.js arrives through a dynamic import of its
// .mjs build. `whenReady` is the gate for that, and is what anything touching a
// possibly-polyfilled class awaits.

import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';

const { debugLog, errorLog, infoLog, warnLog } = getLogs('WebCodecsCompat');

const LIBAV_FILE = 'libav-6.10.9.0-vp9-opus-avf-simd.mjs';
const POLYFILL_FILE = 'libavjs-webcodecs-polyfill.js';

const POLYFILL_CLASS_NAMES = [
    'EncodedAudioChunk', 'AudioData', 'AudioDecoder', 'AudioEncoder',
    'EncodedVideoChunk', 'VideoFrame', 'VideoDecoder', 'VideoEncoder',
] as const;

export type WebCodecsLevel = 'none' | 'vp9' | 'full';
export type WebCodecsLevelOverride = 'auto' | WebCodecsLevel;

/** What a caller is about to use, so the gate can stay shut for the rest. */
export type WebCodecsComponent = 'video-encode' | 'video-decode' | 'audio-encode' | 'audio-decode';

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
    private static _config: WebCodecsCompatConfig | null = null;
    private static _isReady = true;
    private static _whenReady: Promise<void> | null = null;
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
        return this._whenReady ?? READY;
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

    /** Records the level for this realm. Nothing is fetched until a component that
     *  the level actually affects asks for {@link whenReadyFor}. */
    static init(config: WebCodecsCompatConfig): void {
        if (this._config && config.level !== this._config.level) {
            warnLog?.log(`init: already at '${this._config.level}', ignoring '${config.level}'`);
            return;
        }

        this._config = config;
        this._level = config.level;
    }

    /** The gate, and the trigger: awaiting this is what starts the download, so a
     *  realm that never encodes video never pays for it. */
    static whenReadyFor(component: WebCodecsComponent): Promise<void> {
        if (!this.affects(component))
            return READY;

        this._whenReady ??= this.load().catch((error: unknown) => {
            // Staying native is worse than the polyfill but better than no media;
            // callers see it through `level`, which drops back to 'none'.
            errorLog?.log(`load('${this._level}') failed, staying native:`, error);
            this._level = 'none';
        }).finally(() => {
            this._isReady = true;
        });
        this._isReady = false;

        return this._whenReady;
    }

    /** Whether the level in force changes anything for this component. `vp9`
     *  replaces the video encoder only, so audio is untouched there. */
    static affects(component: WebCodecsComponent): boolean {
        if (this._level === 'none')
            return false;

        return this._level === 'full' || component === 'video-encode';
    }

    // Private methods

    private static async load(): Promise<void> {
        const config = this._config!;
        const startedAt = performance.now();
        const base = config.baseUrl.replace(/\/$/, '');
        // The .mjs build self-locates its wasm glue from import.meta.url, so the
        // folder layout is the whole contract.
        const libav = await import(/* webpackIgnore: true */ `${base}/${LIBAV_FILE}`) as { default: unknown };
        (globalThis as { LibAV?: unknown }).LibAV = libav.default;
        this._level = config.level;
        const elapsed = (): number => Math.round(performance.now() - startedAt);
        if (config.level !== 'full') {
            infoLog?.log(`init: '${config.level}' ready in ${elapsed()}ms`);

            return;
        }

        // Only `full` needs the polyfill; `vp9` uses Vp9Encoder, which drives
        // libav.js directly.
        await loadScript(`${base}/${POLYFILL_FILE}`);
        const loaded = (globalThis as { LibAVWebCodecs?: WebCodecsPolyfillClasses }).LibAVWebCodecs;
        if (!loaded)
            throw new Error(`${POLYFILL_FILE} loaded but defined no LibAVWebCodecs`);

        this._classes = loaded;
        installGlobals(loaded);
        // Ponyfill mode: the polyfill's own installer fills in MISSING classes only.
        await loaded.load({ polyfill: false, libavOptions: { nothreads: true } });
        infoLog?.log(`init: 'full' ready in ${elapsed()}ms`);
    }
}

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
