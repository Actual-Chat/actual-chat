// Diagnostics-injected VideoEncoder.configure() failure, backed by localStorage.
// Emulates browsers where isConfigSupported() says a codec is supported but the
// real configure() throws (Firefox WebCodecs H.264, bugzil.la/1918769). Raw value:
// '' (off), '<category>' (fail probe + worker), or '<category>:worker' (probe
// passes — exercises the runtime exclusion/re-pick path in isolation).

const KEY = 'video.debug.encoderFailInjection';
const CATEGORIES = ['h264', 'hevc', 'av1', 'vp9'];

export type EncoderFailSite = 'probe' | 'worker';

export function getEncoderFailInjection(): string {
    try {
        const raw = globalThis.localStorage.getItem(KEY) ?? '';
        return isValidInjection(raw) ? raw : '';
    } catch {
        return '';
    }
}

export function setEncoderFailInjection(raw: string): void {
    try {
        if (raw === '' || !isValidInjection(raw)) {
            globalThis.localStorage.removeItem(KEY);
            return;
        }

        globalThis.localStorage.setItem(KEY, raw);
    } catch {
        // Debug-only setting; ignore storage failures in private/sandboxed contexts.
    }
}

// Pure so worker code can apply the config-threaded raw value without storage.
export function matchesEncoderFailInjection(
    raw: string | undefined,
    category: string,
    site: EncoderFailSite,
): boolean {
    if (!raw)
        return false;

    const [failCategory, scope] = raw.split(':');
    if (failCategory !== category)
        return false;

    return scope === 'worker' ? site === 'worker' : true;
}

export function newInjectedEncoderConfigureError(category: string): Error {
    return new DOMException(
        `Operation is not supported (debug injection for ${category})`,
        'NotSupportedError');
}

function isValidInjection(raw: string): boolean {
    const parts = raw.split(':');
    if (parts.length > 2)
        return false;

    return CATEGORIES.includes(parts[0]) && (parts.length === 1 || parts[1] === 'worker');
}
