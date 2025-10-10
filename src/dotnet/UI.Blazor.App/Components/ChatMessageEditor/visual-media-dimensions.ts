export class VisualMediaDimensions {
    public static async getDimensions(previewUrl: string, mimeType: string): Promise<{ width: number, height: number } | null> {
        return new Promise((resolve, reject) => {
            if (mimeType.startsWith("image/")) {
                const img = new Image();
                img.onload = () => {
                    resolve({ width: img.naturalWidth, height: img.naturalHeight });
                };
                img.onerror = (err) => reject(err);
                img.src = previewUrl;
            } else if (mimeType.startsWith("video/")) {
                const video = document.createElement("video");
                video.preload = "metadata";
                video.onloadedmetadata = () => {
                    resolve({ width: video.videoWidth, height: video.videoHeight });
                };
                video.onerror = (err) => reject(err);
                video.src = previewUrl;
            } else {
                resolve(null);
            }
        });
    }
}
