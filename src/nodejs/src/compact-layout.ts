// Global registry of "reasons" that demand a compact chat layout
// (landscape mobile, on-screen keyboard, …). Panels subscribe to the events
// AND read the current Set on construction so late-mounted panels start
// in the right state.

const reasons = new Set<string>();

export const CompactLayout = {
    get reasons(): ReadonlySet<string> {
        return reasons;
    },

    get hasReasons(): boolean {
        return reasons.size > 0;
    },

    request(reason: string): void {
        if (reasons.has(reason))
            return;
        reasons.add(reason);
        document.dispatchEvent(new CustomEvent('chat-layout:request-compact', { detail: { reason } }));
    },

    release(reason: string): void {
        if (!reasons.has(reason))
            return;
        reasons.delete(reason);
        document.dispatchEvent(new CustomEvent('chat-layout:release-compact', { detail: { reason } }));
    },
};
