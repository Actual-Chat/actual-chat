export function selectAndGet(inputRef: HTMLInputElement) {
    inputRef.select();
    return inputRef.value;
}

// Writes both a plain-text and an HTML flavor to the clipboard so external apps get readable
// text while our editor can reconstruct mentions from the HTML's data-voxt-markup attribute.
export async function writeRich(plainText: string, html: string): Promise<void> {
    try {
        if (typeof ClipboardItem !== 'undefined') {
            const item = new ClipboardItem({
                'text/plain': new Blob([plainText], { type: 'text/plain' }),
                'text/html': new Blob([html], { type: 'text/html' }),
            });
            await navigator.clipboard.write([item]);
            return;
        }
    } catch {
        // Falls through to plain-text write below.
    }
    // Plain-text fallback. On MAUI/Android writeText is overridden to route to the native clipboard,
    // so rich copy degrades to plain text there. Guard against insecure-context throws.
    try {
        await navigator.clipboard.writeText(plainText);
    } catch (e) {
        console.warn('writeRich: clipboard write failed', e);
    }
}
