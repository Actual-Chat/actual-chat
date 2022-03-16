/** Async version of setTimeout */
export function delayAsync(timeout: number): Promise<void> {
    return new Promise<void>(resolve => setTimeout(resolve, timeout));
}
