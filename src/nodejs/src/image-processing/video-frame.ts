import { PromiseSourceWithTimeout } from 'actuallab-core';

const FRAME_TIME_S = 0.5;
const CAPTURE_TIMEOUT_MS = 10_000;

/** Decodes a video's frame at ~0.5s (the editor previews the same one) into a PNG no longer than `maxLongSide`. */
export async function captureVideoFrame(url: string, maxLongSide: number): Promise<Blob> {
    const video = document.createElement('video');
    video.muted = true;
    video.playsInline = true;
    video.preload = 'auto';
    try {
        await whenEvent('loadeddata', () => video.src = url);
        // The first frame is often black, while seeking past a short clip's end yields no frame at all
        const duration = Number.isFinite(video.duration) ? video.duration : 0;
        const time = duration > 0 ? Math.min(FRAME_TIME_S, duration / 2) : 0;
        if (time > 0)
            await whenEvent('seeked', () => video.currentTime = time);

        const width = video.videoWidth;
        const height = video.videoHeight;
        if (width <= 0 || height <= 0)
            throw new Error('captureVideoFrame: the video has no dimensions.');

        const scale = Math.min(1, maxLongSide / Math.max(width, height));
        const canvas = new OffscreenCanvas(
            Math.max(1, Math.round(width * scale)),
            Math.max(1, Math.round(height * scale)));
        const context = canvas.getContext('2d')!;
        context.imageSmoothingQuality = 'high';
        context.drawImage(video, 0, 0, canvas.width, canvas.height);
        return await canvas.convertToBlob({ type: 'image/png' });
    }
    finally {
        // Without this the element keeps its decoder and the file handle until GC
        video.removeAttribute('src');
        video.load();
    }

    function whenEvent(eventName: 'loadeddata' | 'seeked', start: () => void): Promise<void> {
        const result = new PromiseSourceWithTimeout<void>();
        const onEvent = () => result.resolve();
        const onError = () => result.reject(new Error(`captureVideoFrame: failed while waiting for '${eventName}'.`));
        video.addEventListener(eventName, onEvent, { once: true });
        video.addEventListener('error', onError, { once: true });
        result.setTimeout(CAPTURE_TIMEOUT_MS);
        start();
        return result.finally(() => {
            video.removeEventListener(eventName, onEvent);
            video.removeEventListener('error', onError);
        });
    }
}
