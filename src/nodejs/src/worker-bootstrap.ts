import { initWorkerLogging } from 'actuallab-core';

export function bootstrapWorker(importMain: () => Promise<unknown>): void {
    const target = self as unknown as Worker;
    const queuedMessages: MessageEvent[] = [];
    const queuedMessageErrors: MessageEvent[] = [];

    target.onmessage = event => queuedMessages.push(event);
    target.onmessageerror = event => queuedMessageErrors.push(event);

    void (async () => {
        await initWorkerLogging();
        await importMain();

        const onMessage = target.onmessage;
        const onMessageError = target.onmessageerror;
        for (const event of queuedMessages)
            await onMessage?.call(target, event);
        for (const event of queuedMessageErrors)
            await onMessageError?.call(target, event);
    })();
}
