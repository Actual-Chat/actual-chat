// Offset in ms: serverNow ≈ Date.now() + offset
let offsetMs = 0;
type OffsetListener = (offsetMs: number) => void;
const listeners: OffsetListener[] = [];

export class ServerClock {
    static get offsetMs(): number { return offsetMs; }
    /** Returns server-aligned epoch ms */
    static now(): number { return Date.now() + offsetMs; }
    /** Called from C# ServerTimeSync whenever offset is updated */
    static updateOffset(newOffsetMs: number): void {
        offsetMs = newOffsetMs;
        for (const fn of listeners) fn(newOffsetMs);
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
