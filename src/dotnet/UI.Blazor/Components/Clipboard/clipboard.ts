export function selectAndCopy(inputRef: HTMLInputElement, text: string | null) {
    inputRef.select();
    void navigator.clipboard.writeText(text ?? inputRef.value);
}
