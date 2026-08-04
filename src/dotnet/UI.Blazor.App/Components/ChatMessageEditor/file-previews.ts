// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/prefer-promise-reject-errors */
export class FilePreviews {
    public static getDimensions(
        previewUrl: string,
        mimeType: string,
    ): Promise<{ width: number, height: number, durationMs: number } | null> {
        return new Promise((resolve, reject) => {
            if (mimeType.startsWith('image/')) {
                const img = new Image();
                img.onload = () => {
                    resolve({ width: img.naturalWidth, height: img.naturalHeight, durationMs: 0 });
                };
                img.onerror = (err) => reject(err);
                img.src = previewUrl;
            } else if (mimeType.startsWith('video/')) {
                const video = document.createElement('video');
                video.preload = 'metadata';
                video.onloadedmetadata = () => {
                    const duration = video.duration;
                    const durationMs = Number.isFinite(duration) && duration > 0 ? Math.round(duration * 1000) : 0;
                    resolve({ width: video.videoWidth, height: video.videoHeight, durationMs });
                };
                video.onerror = (err) => reject(err);
                video.src = previewUrl;
            } else {
                resolve(null);
            }
        });
    }
}
