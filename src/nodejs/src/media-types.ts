// !!! keep in sync with MediaTypeExt.cs
const supportedImageTypes = ["image/png", "image/jpeg", "image/svg+xml", "image/webp", "image/gif", "image/bmp", "image/heif", "image/heic", "image/heif", "image/avif"];
const supportedVideoTypes = ["video/mp4", "video/vp8", "video/vp9", "video/av1"];

export function isSupportedImage(mediaType: string) {
    return supportedImageTypes.includes(mediaType.toLowerCase());
}

export function isSupportedVideo(mediaType: string) {
    return supportedVideoTypes.includes(mediaType.toLowerCase());
}
