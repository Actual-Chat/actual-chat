// Which surface paints the self-preview. WebKit gets the canvas painter: it
// measured ~6.5 points cheaper on an iPhone 13 Pro (painting moves into
// WebKit.GPU, but backboardd no longer composites a video layer) and it
// sidesteps WebKit bug 230922. See docs/ui/components.md.
//
// `video.debug.previewCanvas`: '1' forces canvas, '0' forces the generated
// track, absent uses the default.

import { DeviceInfo } from 'device-info';
import { WebCodecsCompat } from 'web-codecs-compat/init';

const KEY = 'video.debug.previewCanvas';

/** At `full` the worker reports ImageBitmaps rather than feeding a generator, so a
 *  track-backed preview would sit empty over the canvas that actually gets painted. */
export function isPreviewCanvasPreferred(): boolean {
    return readOverride() ?? (DeviceInfo.isWebKit || WebCodecsCompat.isPolyfilledRealm);
}

export function setPreviewCanvasOverride(isCanvas: boolean | null): void {
    try {
        if (isCanvas === null)
            globalThis.localStorage.removeItem(KEY);
        else
            globalThis.localStorage.setItem(KEY, isCanvas ? '1' : '0');
    } catch { /* ignore */ }
}

// Private methods

function readOverride(): boolean | null {
    try {
        const raw = globalThis.localStorage.getItem(KEY);
        if (raw === '1')
            return true;
        if (raw === '0')
            return false;

        return null;
    } catch {
        return null;
    }
}
