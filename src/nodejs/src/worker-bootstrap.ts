import { initWorkerLogging, applyLogLevelOverrides } from 'actuallab-core';

export function bootstrapWorker(importMain: () => Promise<unknown>): void {
    const target = self as unknown as Worker;
    const queuedMessages: MessageEvent[] = [];
    const queuedMessageErrors: MessageEvent[] = [];

    target.onmessage = event => queuedMessages.push(event);
    target.onmessageerror = event => queuedMessageErrors.push(event);

    void (async () => {
        await initWorkerLogging();
        // Seed the worker with the creator's logLevel overrides (passed as ?ll=)
        // BEFORE importMain — getLogs() freezes each logger at module-load level.
        applyCreatorLogLevels();
        await importMain();

        const onMessage = target.onmessage;
        const onMessageError = target.onmessageerror;
        for (const event of queuedMessages)
            await onMessage?.call(target, event);
        for (const event of queuedMessageErrors)
            await onMessageError?.call(target, event);
    })();
}

function applyCreatorLogLevels(): void {
    try {
        const raw = new URLSearchParams(self.location.search).get('ll');
        if (raw)
            applyLogLevelOverrides(JSON.parse(decodeURIComponent(raw)) as Record<string, number>);
    } catch {
        // Malformed/absent override snapshot — ignore, keep restored levels.
    }
}
