// Shared `?bgBlur=…` query-param reader.
//
// The receiver-side bg blur lives in VideoPlayer / the player worker, the
// sender-side preview blur lives in VideoStreamingPreview / RecorderPreviewView.
// Both need to honor `?bgBlur=off` so the kill-switch behaves consistently
// across both surfaces; both call into here.

export type BgBlurOverride = 'webgpu' | 'webgl' | 'webgl-kawase' | 'canvas2d' | 'off';

export function readBgBlurOverride(): BgBlurOverride | undefined {
    try {
        const v = new URLSearchParams(globalThis.location.search).get('bgBlur');
        if (v === 'webgpu' || v === 'webgl' || v === 'webgl-kawase' || v === 'canvas2d' || v === 'off')
            return v;
    } catch { /* ignore */ }
    return undefined;
}

export function isBgBlurOff(): boolean {
    return readBgBlurOverride() === 'off';
}
