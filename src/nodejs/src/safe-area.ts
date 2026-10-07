export interface SafeAreaInsets {
    top: number;
    right: number;
    bottom: number;
    left: number;
}

/** A window size in CSS px. */
export interface Viewport {
    width: number;
    height: number;
}

/** A phone's screen geometry. `viewport` is zero for the uniform debug presets, which fit any window. */
export interface SafeAreaPreset {
    name: string;
    viewport: Viewport;
    insets: SafeAreaInsets;
    cornerRadius: number;
}

/** An element's box in viewport coordinates - the shape of DOMRect and of Playwright's boundingBox(). */
export interface Box {
    x: number;
    y: number;
    width: number;
    height: number;
}

export interface SafeAreaViolation {
    element: Element;
    box: Box;
    problems: string[];
}

const NoViewport: Viewport = { width: 0, height: 0 };

/** Safe areas: reading the insets the app lays out around, and emulating a phone's on a desktop
 *  browser - its viewport, its insets and the radius of its rounded display corners, all in CSS px.
 *  The presets and the geometry check are shared by debugUI.showSafeAreas and the TS e2e suite, so
 *  the two never disagree about what "an iPhone" is. Nothing here touches the DOM at load, which is
 *  what lets the e2e suite import it into Node. */
export class SafeAreas {
    // iPhone numbers are Apple's device metrics (points = CSS px). Android's are the framework defaults:
    // a 28dp status bar next to a hole-punch cutout and a 24dp gesture navigation bar; the corner radius
    // is the Pixel 8's rounded_corner_radius in dp. `uniform` and `none` are what showSafeAreas(true) and
    // showSafeAreas(false) have always meant: 34px on every side, and 0px on every side.
    public static readonly Presets = {
        uniform: {
            name: 'Uniform 34px',
            viewport: NoViewport,
            insets: { top: 34, right: 34, bottom: 34, left: 34 },
            cornerRadius: 0,
        },
        none: {
            name: 'None',
            viewport: NoViewport,
            insets: { top: 0, right: 0, bottom: 0, left: 0 },
            cornerRadius: 0,
        },
        iphone15: {
            name: 'iPhone 15',
            viewport: { width: 393, height: 852 },
            insets: { top: 59, right: 0, bottom: 34, left: 0 },
            cornerRadius: 55,
        },
        iphone15Landscape: {
            name: 'iPhone 15 (landscape)',
            viewport: { width: 852, height: 393 },
            insets: { top: 0, right: 59, bottom: 21, left: 59 },
            cornerRadius: 55,
        },
        iphoneSE: {
            name: 'iPhone SE',
            viewport: { width: 375, height: 667 },
            insets: { top: 0, right: 0, bottom: 0, left: 0 },
            cornerRadius: 0,
        },
        pixel8: {
            name: 'Pixel 8',
            viewport: { width: 412, height: 915 },
            insets: { top: 28, right: 0, bottom: 24, left: 0 },
            cornerRadius: 29,
        },
    } satisfies Record<string, SafeAreaPreset>;

    private static readonly StorageKey = 'ui.debug.safeAreas';
    private static readonly StyleId = 'safe-area-emulation';
    private static readonly InteractiveSelector =
        'a[href], button, input, select, textarea, [role="button"], [contenteditable="true"]';
    // Sub-pixel layout noise: a box 0.3px into an inset is not a bug.
    private static readonly Tolerance = 0.5;

    /** The emulation currently on the document, if any. */
    public static get activeEmulation(): SafeAreaPreset | null {
        return document.getElementById(this.StyleId) === null ? null : this.getPersistedEmulation();
    }

    /** Overflow padding for a floating element, so it never lands under a display cutout or home
     *  indicator. Reads the CSS variables rather than env(), so debugUI.showSafeAreas applies here too. */
    public static getPadding(gap = 0): SafeAreaInsets {
        const style = getComputedStyle(document.body);
        const inset = (name: string) => gap + (Number.parseFloat(style.getPropertyValue(name)) || 0);
        return {
            top: inset('--safe-area-top'),
            right: inset('--safe-area-right'),
            bottom: inset('--safe-area-bottom'),
            left: inset('--safe-area-left'),
        };
    }

    /** Takes any string, not just a preset name, because the console passes whatever it was given;
     *  an unknown one throws with the known names. */
    public static resolvePreset(preset: SafeAreaPreset | string): SafeAreaPreset {
        if (typeof preset !== 'string')
            return preset;
        if (!this.isPresetName(preset))
            throw new Error(`Unknown safe-area preset '${preset}'; known: ${Object.keys(this.Presets).join(', ')}`);

        return this.Presets[preset];
    }

    /** Why a box would be unusable on that phone: it runs under an inset, or a rounded corner cuts it.
     *  Empty when it's fine. `viewport` is the real window when it differs from the preset's own. */
    public static getProblems(box: Box, preset: SafeAreaPreset, viewport: Viewport = preset.viewport): string[] {
        const { insets, cornerRadius: r } = preset;
        const tolerance = this.Tolerance;
        const right = box.x + box.width;
        const bottom = box.y + box.height;
        const problems: string[] = [];
        const over = (amount: number, where: string) => {
            if (amount > tolerance)
                problems.push(`${Math.round(amount)}px under the ${where} inset`);
        };
        over(insets.top - box.y, 'top');
        over(bottom - (viewport.height - insets.bottom), 'bottom');
        over(insets.left - box.x, 'left');
        over(right - (viewport.width - insets.right), 'right');

        if (r > 0) {
            // Each corner's cut-off region is the R×R square minus the quarter circle. It grows toward the
            // corner, so the box's nearest point to that corner decides for the whole box.
            const corners: [string, number, number][] = [
                ['top-left', box.x, box.y],
                ['top-right', viewport.width - right, box.y],
                ['bottom-right', viewport.width - right, viewport.height - bottom],
                ['bottom-left', box.x, viewport.height - bottom],
            ];
            for (const [name, dx, dy] of corners) {
                if (dx >= r - tolerance || dy >= r - tolerance)
                    continue;

                const ex = r - dx;
                const ey = r - dy;
                if (Math.hypot(ex, ey) > r + tolerance)
                    problems.push(`clipped by the ${name} corner`);
            }
        }
        return problems;
    }

    /** Stylesheet text that paints the emulation over everything: black rounded corners, and a dashed
     *  line along the safe area's edge - a line rather than a tint, so what the app paints under the
     *  insets keeps its real colour. With `mustForceInsets`, the safe-area variables are set too, for a
     *  browser whose env() can't be overridden (the e2e suite overrides env() itself, so it leaves them
     *  alone). */
    public static getEmulationCss(preset: SafeAreaPreset, mustForceInsets: boolean): string {
        const { insets, cornerRadius: r } = preset;
        const corner = (at: string) =>
            `radial-gradient(circle at ${at}, transparent ${r - 0.5}px, #000 ${r + 0.5}px)`;
        const px = (v: number) => `${v}px`;
        const rules = [
            `html::before { content: ''; position: fixed; inset: 0; z-index: 2147483600; pointer-events: none;`
            + ` background-image: ${corner('100% 100%')}, ${corner('0 100%')}, ${corner('0 0')}, ${corner('100% 0')};`
            + ` background-size: ${px(r)} ${px(r)}; background-repeat: no-repeat;`
            + ` background-position: left top, right top, right bottom, left bottom; }`,
            `html::after { content: ''; position: fixed; z-index: 2147483599; pointer-events: none;`
            + ` inset: ${px(insets.top)} ${px(insets.right)} ${px(insets.bottom)} ${px(insets.left)};`
            + ` box-sizing: border-box; border: 1px dashed rgba(255, 59, 48, 0.6); }`,
        ];
        if (mustForceInsets)
            rules.push(
                `body { --safe-area-top: ${px(insets.top)}; --safe-area-right: ${px(insets.right)};`
                + ` --safe-area-bottom: ${px(insets.bottom)}; --safe-area-left: ${px(insets.left)}; }`);
        return rules.join('\n');
    }

    /** Puts the emulation on the current document - or takes it off with null - and remembers it in
     *  localStorage, so the next load restores it before Blazor starts (splash and skeletons included). */
    public static emulate(preset: SafeAreaPreset | string | null): SafeAreaPreset | null {
        const resolved = preset === null ? null : this.resolvePreset(preset);
        document.getElementById(this.StyleId)?.remove();
        try {
            if (resolved === null)
                localStorage.removeItem(this.StorageKey);
            else
                localStorage.setItem(this.StorageKey, JSON.stringify(resolved));
        } catch {
            // Storage is unavailable in some embedded contexts; the emulation still applies for this load.
        }
        if (resolved === null)
            return null;

        const style = document.createElement('style');
        style.id = this.StyleId;
        style.textContent = this.getEmulationCss(resolved, true);
        document.head.appendChild(style);
        return resolved;
    }

    public static restoreEmulation(): void {
        this.emulate(this.getPersistedEmulation());
    }

    /** Interactive elements of `root` that are actually on screen - inside the window and not covered by
     *  something else - and that the preset would hide under an inset or clip in a corner. */
    public static findViolations(preset: SafeAreaPreset, root: ParentNode = document): SafeAreaViolation[] {
        const viewport = { width: window.innerWidth, height: window.innerHeight };
        const violations: SafeAreaViolation[] = [];
        for (const element of root.querySelectorAll(this.InteractiveSelector)) {
            const rect = element.getBoundingClientRect();
            if (!impl(element, rect))
                continue;

            const box = { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
            const problems = this.getProblems(box, preset, viewport);
            if (problems.length > 0)
                violations.push({ element, box, problems });
        }
        return violations;

        // Is it on screen? Closed side panels sit translated past the window's edge, file inputs at -9999px,
        // hotkeys and menus behind whatever is open: a hit test at the visible part's centre is what tells a
        // control someone can tap from one that only has a box.
        function impl(element: Element, rect: DOMRect): boolean {
            if (rect.width <= 0 || rect.height <= 0)
                return false;

            const left = Math.max(rect.left, 0);
            const top = Math.max(rect.top, 0);
            const right = Math.min(rect.right, viewport.width);
            const bottom = Math.min(rect.bottom, viewport.height);
            if (right - left < 1 || bottom - top < 1)
                return false;

            const hit = document.elementFromPoint((left + right) / 2, (top + bottom) / 2);
            return hit !== null && element.contains(hit);
        }
    }

    // Private methods

    private static isPresetName(value: string): value is keyof typeof SafeAreas.Presets {
        return Object.keys(this.Presets).includes(value);
    }

    private static getPersistedEmulation(): SafeAreaPreset | null {
        try {
            const raw = localStorage.getItem(this.StorageKey);
            return raw === null ? null : JSON.parse(raw) as SafeAreaPreset;
        } catch {
            return null;
        }
    }
}

export type SafeAreaPresetName = keyof typeof SafeAreas.Presets;
