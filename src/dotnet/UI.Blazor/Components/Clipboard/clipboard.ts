export function selectAndCopy(inputRef: HTMLInputElement, text: string | null) {
    inputRef.select();
    void navigator.clipboard.writeText(text ?? inputRef.value);
}

export function selectAndGet(inputRef: HTMLInputElement) {
    inputRef.select();
    return inputRef.value;
}
