// Offset in ms: serverNow ≈ Date.now() + offset
let offsetMs = 0;
type OffsetListener = (offsetMs: number) => void;
const listeners: OffsetListener[] = [];

export class ServerClock {
    static get offsetMs(): number { return offsetMs; }
    /** Returns server-aligned epoch ms */
    static now(): number { return Date.now() + offsetMs; }
    /** Called from C# ServerTimeSync with the current server time (epoch ms) */
    static updateOffset(serverNowMs: number): void {
        offsetMs = serverNowMs - Date.now();
        for (const fn of listeners) fn(offsetMs);
    }
    /** Subscribe to offset changes. Returns unsubscribe function. */
    static onOffsetChanged(fn: OffsetListener): () => void {
        listeners.push(fn);
        return () => {
            const idx = listeners.indexOf(fn);
            if (idx >= 0) listeners.splice(idx, 1);
        };
    }
}
