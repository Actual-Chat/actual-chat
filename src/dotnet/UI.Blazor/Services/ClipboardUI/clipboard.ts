export function selectAndGet(inputRef: HTMLInputElement) {
    inputRef.select();
    return inputRef.value;
}

// Writes both a plain-text and an HTML flavor to the clipboard so external apps get readable
// text while our editor can reconstruct mentions from the HTML's data-voxt-markup attribute.
export async function writeRich(plainText: string, html: string): Promise<void> {
    try {
        if (typeof ClipboardItem !== "undefined" && navigator.clipboard?.write) {
            const item = new ClipboardItem({
                "text/plain": new Blob([plainText], { type: "text/plain" }),
                "text/html": new Blob([html], { type: "text/html" }),
            });
            await navigator.clipboard.write([item]);
            return;
        }
    } catch {
        // Falls through to plain-text write below.
    }
    await navigator.clipboard.writeText(plainText);
}
