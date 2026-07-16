import { base64Decode } from 'actuallab-rpc';

// Draws a base64 JPEG frame (pushed from native code on Mac Catalyst) onto the
// canvas, resizing it to the frame when needed. Returns false when nothing was
// drawn: decode failure, no 2d context, or isCancelled() flipped during the
// async decode.
export async function renderJpegFrame(
    canvas: HTMLCanvasElement,
    base64Jpeg: string,
    isCancelled?: () => boolean,
): Promise<boolean> {
    const bytes = base64Decode(base64Jpeg);
    const blob = new Blob([bytes.buffer as ArrayBuffer], { type: 'image/jpeg' });
    const bitmap = await createImageBitmap(blob);
    if (isCancelled?.()) {
        bitmap.close();
        return false;
    }

    const ctx = canvas.getContext('2d');
    if (!ctx) {
        bitmap.close();
        return false;
    }

    if (canvas.width !== bitmap.width || canvas.height !== bitmap.height) {
        canvas.width = bitmap.width;
        canvas.height = bitmap.height;
    }
    ctx.drawImage(bitmap, 0, 0);
    bitmap.close();
    return true;
}
