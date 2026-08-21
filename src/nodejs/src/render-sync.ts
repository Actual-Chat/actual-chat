import { fastRaf20 } from 'fast-raf';

// Blazor's WebView host applies each render batch the instant it arrives, so a burst of
// transcript updates lands in as many separate frames, none of them on the grid the rest of
// the app writes on. Holding batches to a 20Hz step - which divides the 100ms animation grid
// exactly - puts those writes on shared instants and lets several share one rendering update,
// measured at 3.13 batches per flush. The flush has to stay in the write phase.
//
// OFF by default: three variants were measured on an iPhone 13 Pro, 30s in call, and none beat
// leaving batches alone. WebContent cores and forced synchronous layout on its main thread,
// against a no-deferral baseline of 0.316 cores / 0ms forced:
//
//   50ms, flush in the read phase   0.373   1461ms   writes landed among that frame's readers
//   50ms, flush in the write phase  0.331    595ms   ordering fixed, still no better than off
//   50ms, queue interop too         0.378   1551ms   interop reads moved after the batch writes
//
// Coalescing does cut updateLayoutAndStyleIfNeededRecursive by up to 26%, but it converts
// scheduled layout into forced layout by at least as much: holding writes back puts them nearer
// the reads that follow. Kept because the ceiling was only ever ~0.015 cores and a gate that
// defers solely while a call is live may still clear it - toggle at runtime rather than
// rebuilding, so one call can be A/B'd against itself.
let isEnabled = false;

const renderBatchPrefix = '__bwv:["RenderBatch"';
// A backgrounded WebView stops painting, so a flush scheduled on a frame may not arrive.
// This bounds both the queue and the burst that lands when it resumes.
const maxPendingCount = 32;
// Where each WebView host keeps the handler that blazor.webview.js registered through
// `window.external.receiveMessage`. The host calls it by name for every message, so
// replacing it is the seam. Wrapping `receiveMessage` itself would be tidier but is
// unreachable: it has already been called by the time any bundle code runs.
const handlerGlobals = ['__dispatchMessageCallback', '__receiveMessageCallback'];
// Android is the odd one out: its handler hangs off window.external rather than the global
// scope, so the same seam needs a second lookup with a different root.
const externalHandlerName = '__callback';

type MessageHandler = (message: string) => void;

let pending: string[] | null = null;
let pendingHandler: MessageHandler | null = null;

function flush(): void {
    const batch = pending;
    const handler = pendingHandler;
    if (batch === null || handler === null)
        return;

    // Cleared before applying: a batch that throws must not strand the ones behind it,
    // and the handler can re-enter through interop.
    pending = null;
    for (const message of batch)
        handler(message);
}

function onMessage(message: string, handler: MessageHandler): void {
    // Ahead of the deferral so observers see arrival order, which is what a lost message shows up in.
    RenderSync.observer?.(message);
    if (!isEnabled || !message.startsWith(renderBatchPrefix)) {
        // Interop must stay ordered against the DOM it reads: an ElementReference is
        // resolved by querySelector when the call runs, so it would silently miss an
        // element still sitting in the queue.
        flush();
        handler(message);
        return;
    }

    pendingHandler = handler;
    if (pending === null) {
        pending = [message];
        fastRaf20({ write: flush });
        return;
    }

    pending.push(message);
    if (pending.length >= maxPendingCount)
        flush();
}

export class RenderSync {
    public static observer: MessageHandler | null = null;

    public static get isEnabled(): boolean {
        return isEnabled;
    }
    public static set isEnabled(value: boolean) {
        if (isEnabled === value)
            return;

        isEnabled = value;
        // Whatever is already queued would otherwise wait for a batch that may never come.
        if (!value)
            flush();
    }

    // No-op on every host that isn't a MAUI WebView - including WASM, where a render
    // batch must be applied synchronously because it holds a Mono heap pointer.
    //
    // The handler is normally still absent here: blazor.webview.js registers it from
    // Blazor.start(), which runs after this deferred bundle. So the usual path is the
    // accessor below, which wraps whatever gets assigned, whenever that happens.
    public static init(): string | null {
        const globals = globalThis as unknown as Record<string, unknown>;
        const external = (globalThis as unknown as { external?: Record<string, unknown> }).external;
        for (const name of handlerGlobals) {
            if (typeof globals[name] === 'function') {
                globals[name] = RenderSync.wrap(globals[name] as MessageHandler);
                return name;
            }
        }
        if (external && typeof external[externalHandlerName] === 'function') {
            external[externalHandlerName] = RenderSync.wrap(external[externalHandlerName] as MessageHandler);
            return externalHandlerName;
        }

        for (const name of handlerGlobals)
            RenderSync.hookOnAssign(globals, name);
        if (external)
            RenderSync.hookOnAssign(external, externalHandlerName);
        return null;
    }

    // Private methods

    private static wrap(handler: MessageHandler): MessageHandler {
        return (message: string) => onMessage(message, handler);
    }

    private static hookOnAssign(target: Record<string, unknown>, name: string): void {
        let wrapped: MessageHandler | undefined;
        Object.defineProperty(target, name, {
            configurable: true,
            get: () => wrapped,
            set: (handler: MessageHandler) => {
                wrapped = RenderSync.wrap(handler);
                // Published only once it actually fires: "installed but never triggered"
                // is the failure this whole indirection exists to make visible. Always on the
                // global scope, even when the seam that fired was window.external's.
                (globalThis as unknown as Record<string, unknown>)['renderSyncHook'] = name;
            },
        });
    }
}
