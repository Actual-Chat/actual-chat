import { getLogs } from 'logging';

const { warnLog } = getLogs('VirtualList');

const UpdateIntervalMs = 200;
// How long a momentary event stays lit, and how long a changed value stays highlighted. Longer than
// the refresh interval on purpose: a data request that starts and ends between two ticks would
// otherwise never be drawn at all, and anything drawn for a single tick is too brief to notice.
const HoldMs = 500;

const Colors = {
    // Every glyph is always present and only its colour changes, so the bar never reflows: `off` is
    // what "this isn't happening" looks like, and it has to read as clearly unlit next to `dim`.
    off: '#454b57',
    dim: '#6b7280',
    normal: '#cbd5e1',
    good: '#4ade80',
    warn: '#fbbf24',
    active: '#38bdf8',
    flash: '#ffffff',
};

type Tone = keyof typeof Colors;

export interface VirtualListOverlayStats {
    renderDirection: 'up' | 'down';
    stickyEdge: 'up' | 'down' | null;
    hasStartSpacer: boolean;
    hasEndSpacer: boolean;
    hasVeryFirstItem: boolean;
    hasVeryLastItem: boolean;
    isRequestingData: boolean;
    isRendering: boolean;
    // performance.now() of the last data request / render, or null if there hasn't been one. It keeps
    // the corresponding flag lit for HoldMs after these, so events shorter than a tick still show up.
    lastDataRequestAt: number | null;
    lastRenderAt: number | null;
    // The `[..window../total]` chip. `window` sits between the loaded-end markers — "5/16"
    // (visible/rendered) for InfiniteList, "13(120-170)" (visible, rendered index range) for
    // FiniteList — and `total` follows them, for the lists that know one.
    window: string;
    total: number | null;
    meanItemHeight: number;
    // The standing translation the container carries. Zero at rest; a re-pin follow raises it and a
    // fold returns it, so watching it is how you tell one from the other. Always 0 for FiniteList.
    tOffset: number;
}

// What a live list exposes to its overlay. Structural, like VirtualListDebugTarget, so this module
// stays decoupled from both list classes; the instance is passed in via an `as unknown as` cast.
export interface VirtualListOverlayTarget {
    readonly ref: HTMLElement;
    getOverlayStats(): VirtualListOverlayStats;
}

// Reads back the inline styles both lists write, so the refresh costs no layout.
export function isSpacerShown(spacerRef: HTMLElement): boolean {
    return spacerRef.style.display !== 'none' && parseFloat(spacerRef.style.height) > 0;
}

interface Part {
    id: string;
    text: string;
    tone: Tone;
    // Value parts flash white when they change; counters that move on every scroll frame don't, or
    // the whole line would strobe and the real events would be lost in it.
    mustFlashOnChange: boolean;
    // Drops the gap after this part, so the pieces of the `~(x/y]~` chip read as one token even
    // though they're separate spans (the brackets flash on change, the counters inside must not).
    isTight?: boolean;
    // Reserves a width for the parts whose digit count moves, so the bar doesn't resize every time a
    // counter crosses a power of ten. The glyph parts are all one character and need no help.
    minWidthCh?: number;
    align?: 'center' | 'right';
}

// A one-line metrics readout pinned over each virtual list, for eyeballing list behavior while
// using the app. Off by default; debugUI.showVirtualListOverlay() toggles every live list at once
// and persists the choice in UserAppSettings.
//
// It lives in document.body rather than inside the list: .virtual-list sets `contain: strict`,
// which clips even fixed-position descendants.
export class VirtualListOverlay {
    private static readonly all = new Set<VirtualListOverlay>();
    private static isEnabled = false;
    private static timer: ReturnType<typeof setInterval> | null = null;

    private readonly target: VirtualListOverlayTarget;
    private readonly spans = new Map<string, HTMLElement>();
    private readonly lastChange = new Map<string, { signature: string; at: number }>();
    private element: HTMLElement | null = null;

    constructor(target: VirtualListOverlayTarget) {
        this.target = target;
        VirtualListOverlay.all.add(this);
        this.apply();
        VirtualListOverlay.updateTimer();
    }

    public dispose(): void {
        VirtualListOverlay.all.delete(this);
        this.removeElement();
        VirtualListOverlay.updateTimer();
    }

    public static setEnabled(isEnabled: boolean): void {
        if (VirtualListOverlay.isEnabled === isEnabled)
            return;

        VirtualListOverlay.isEnabled = isEnabled;
        for (const overlay of VirtualListOverlay.all)
            overlay.apply();
        VirtualListOverlay.updateTimer();
    }

    // Private methods

    private static updateTimer(): void {
        const mustRun = VirtualListOverlay.isEnabled && VirtualListOverlay.all.size > 0;
        if (mustRun === (VirtualListOverlay.timer !== null))
            return;

        if (!mustRun) {
            clearInterval(VirtualListOverlay.timer!);
            VirtualListOverlay.timer = null;
            return;
        }

        VirtualListOverlay.timer = setInterval(
            () => {
                const now = performance.now();
                for (const overlay of VirtualListOverlay.all)
                    overlay.update(now);
            },
            UpdateIntervalMs);
    }

    private static buildParts(s: VirtualListOverlayStats, now: number): Part[] {
        const isHeld = (at: number | null): boolean => at != null && now - at < HoldMs;
        const isRequestingData = s.isRequestingData || isHeld(s.lastDataRequestAt);
        const isRendering = s.isRendering || isHeld(s.lastRenderAt);
        const isReverse = s.renderDirection === 'up';
        return [
            // Render direction: one glyph, swapped. Exactly one direction is live at any time and the
            // two arrows are the same width, so this is as stable as an always-both pair would be.
            // Reverse is the notable one, so it gets the loud colour.
            { id: 'r', text: isReverse ? '↑' : '↓', tone: isReverse ? 'active' : 'normal', mustFlashOnChange: true },
            { id: 'q', text: '⇊', tone: isRequestingData ? 'warn' : 'off', mustFlashOnChange: false, isTight: true },
            { id: 'x', text: '⟳', tone: isRendering ? 'active' : 'off', mustFlashOnChange: false },
            // The window chip. Each end carries two independent bits, so it gets two independent
            // encodings: the glyph says whether that end of the data is loaded ([ ] vs ( )), the colour
            // says whether it's the sticky edge. The `~` sit outside the brackets because that is where
            // a spacer actually is — the unloaded region beyond the window on that side. Only the
            // counters between them stay quiet; they move on every scroll frame.
            { id: 'sUp', text: '~', tone: s.hasStartSpacer ? 'warn' : 'off', mustFlashOnChange: true, isTight: true },
            {
                id: 'w0',
                text: s.hasVeryFirstItem ? '[' : '(',
                tone: s.stickyEdge === 'up' ? 'good' : 'normal',
                mustFlashOnChange: true,
                isTight: true,
            },
            {
                id: 'w1',
                text: s.window,
                tone: 'normal',
                mustFlashOnChange: false,
                isTight: true,
                minWidthCh: 8,
                align: 'center',
            },
            {
                id: 'w2',
                text: (s.total == null ? '' : `/${s.total}`) + (s.hasVeryLastItem ? ']' : ')'),
                tone: s.stickyEdge === 'down' ? 'good' : 'normal',
                mustFlashOnChange: true,
                isTight: true,
            },
            { id: 'sDown', text: '~', tone: s.hasEndSpacer ? 'warn' : 'off', mustFlashOnChange: true },
            {
                id: 'h',
                text: `h=${s.meanItemHeight.toFixed(1)}`,
                tone: 'dim',
                mustFlashOnChange: false,
                // Fits `h=NN.N` exactly. Triple-digit means widen by one, but a mean item height only
                // crosses 100px on a genuine content change, so it isn't a source of jitter.
                minWidthCh: 6,
                align: 'right',
            },
            {
                id: 'tOffset',
                text: `Δ=${s.tOffset.toFixed(0)}`,
                // Lit while a translation is standing, since that is the interesting state: at rest it
                // is 0 and says so quietly.
                tone: Math.abs(s.tOffset) >= 1 ? 'warn' : 'dim',
                mustFlashOnChange: true,
                // Fits `Δ=-NNNN`, past the half-screen cap a follow is allowed to reach.
                minWidthCh: 7,
                align: 'right',
            },
        ];
    }

    private apply(): void {
        if (VirtualListOverlay.isEnabled) {
            this.createElement();
            this.update(performance.now());
        }
        else
            this.removeElement();
    }

    private createElement(): void {
        if (this.element)
            return;

        const element = document.createElement('div');
        element.className = 'c-virtual-list-overlay';
        element.style.cssText = [
            'position: fixed',
            'z-index: 2147483000',
            'pointer-events: none',
            'font: 11px/1.4 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
            'white-space: nowrap',
            'overflow: hidden',
            'text-overflow: ellipsis',
            // A floor for the whole bar, so it stops resizing for small content changes. It still grows
            // past this when a list genuinely needs more digits — stable, not rigid.
            'min-width: 27ch',
            'text-align: right',
            'padding: 1px 6px',
            // Opaque on purpose: any transparency lets the list content behind it show through and
            // renders the readout unreadable, which is the one thing an overlay must never be.
            'background: #080a0e',
            'box-shadow: 0 1px 4px rgba(0, 0, 0, 0.5)',
            'border-bottom-left-radius: 4px',
        ].join(';');
        document.body.appendChild(element);
        this.element = element;
    }

    private removeElement(): void {
        this.element?.remove();
        this.element = null;
        this.spans.clear();
        this.lastChange.clear();
    }

    private update(now: number): void {
        const element = this.element;
        if (!element)
            return;

        const rect = this.target.ref.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) {
            element.style.display = 'none';
            return;
        }

        element.style.display = 'block';
        element.style.top = `${rect.top}px`;
        // Pinned to the list's top-right. documentElement.clientWidth, not innerWidth: a fixed element
        // is positioned against the viewport minus the scrollbar, so innerWidth would offset it.
        element.style.right = `${document.documentElement.clientWidth - rect.right}px`;
        element.style.maxWidth = `${rect.width}px`;

        let stats: VirtualListOverlayStats;
        try {
            stats = this.target.getOverlayStats();
        }
        catch (e) {
            warnLog?.log('update: getOverlayStats failed', e);
            return;
        }

        this.render(element, VirtualListOverlay.buildParts(stats, now), now);
    }

    private render(element: HTMLElement, parts: Part[], now: number): void {
        for (const part of parts) {
            let span = this.spans.get(part.id);
            if (!span) {
                span = document.createElement('span');
                span.style.marginRight = part.isTight ? '0' : '6px';
                if (part.minWidthCh != null) {
                    span.style.display = 'inline-block';
                    span.style.minWidth = `${part.minWidthCh}ch`;
                    span.style.textAlign = part.align ?? 'center';
                }
                element.appendChild(span);
                this.spans.set(part.id, span);
            }

            // Tone is part of the signature: with a fixed layout most state changes are colour-only
            // (an arrow lighting up, a bracket becoming the sticky edge), and keying on text alone
            // would mean none of them ever flashed.
            const signature = `${part.text}|${part.tone}`;
            const previous = this.lastChange.get(part.id);
            if (previous?.signature !== signature)
                this.lastChange.set(part.id, { signature, at: now });
            const changedAt = this.lastChange.get(part.id)!.at;
            const isFlashing = part.mustFlashOnChange && previous != null && now - changedAt < HoldMs;

            if (span.textContent !== part.text)
                span.textContent = part.text;
            span.style.color = isFlashing ? Colors.flash : Colors[part.tone];
            // The white text already carries the flash; the wash is only there to catch peripheral
            // vision, so it stays well below the point where it reads as a highlight block.
            span.style.background = isFlashing ? 'rgba(255, 255, 255, 0.09)' : '';
        }
    }
}
