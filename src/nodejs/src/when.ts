export function whenCompleted(): WhenCompleted {
    let resolve: (value: unknown) => void;
    const promise = new Promise(r => {
        resolve = r;
    });
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    promise.complete = resolve;
    // eslint-disable-next-line @typescript-eslint/ban-ts-comment
    // @ts-ignore
    return promise;
}

export interface WhenCompleted extends Promise<void> {
    complete(): void;
}
