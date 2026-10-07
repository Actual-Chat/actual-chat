/**
 * Safe-area emulation for the e2e suite: a phone's viewport, real `env(safe-area-inset-*)` values and
 * the display's rounded corners, so a header, footer or call control that lands under the notch, under
 * the home indicator or in a corner fails here instead of on a device.
 *
 * The presets and the geometry checks live in src/nodejs/src/safe-area.ts, shared with debugUI.
 */

import { expect } from 'vitest';
import type { Locator, Page } from 'playwright';
import { SafeAreas, type SafeAreaPreset } from '../../../src/nodejs/src/safe-area';

export { SafeAreas, type SafeAreaPreset, type SafeAreaPresetName } from '../../../src/nodejs/src/safe-area';

/** A violation as debugUI.checkSafeAreas reports it from inside the page. */
export interface SafeAreaViolationInfo {
    target: string;
    problems: string[];
}

/** Turns the page into that phone. Call it before the first navigation: the corner overlay is an init
 *  script, and the app picks its narrow/wide mode from the width it loads at. The env() override is
 *  Chromium's `Emulation.setSafeAreaInsetsOverride`; it lives as long as the CDP session, which is why
 *  the session is kept open rather than detached. */
export async function emulateSafeAreas(
    page: Page,
    preset: SafeAreaPreset | string = 'iphone15',
): Promise<SafeAreaPreset> {
    const resolved = SafeAreas.resolvePreset(preset);
    if (resolved.viewport.width > 0)
        await page.setViewportSize(resolved.viewport);
    const cdp = await page.context().newCDPSession(page);
    const { insets } = resolved;
    try {
        await cdp.send('Emulation.setSafeAreaInsetsOverride', { insets });
    } catch (e) {
        throw new Error(
            'This Chromium cannot override safe-area insets (Emulation.setSafeAreaInsetsOverride needs Chrome 131+): '
            + (e instanceof Error ? e.message : String(e)),
        );
    }
    await page.addInitScript(impl, SafeAreas.getEmulationCss(resolved, false));
    return resolved;

    // Runs inside the page on every navigation, before <head> may exist.
    function impl(css: string): void {
        const style = document.createElement('style');
        style.textContent = css;
        const head = document.head as HTMLHeadElement | null;
        if (head)
            head.appendChild(style);
        else
            document.addEventListener('DOMContentLoaded', () => document.head.appendChild(style), { once: true });
    }
}

/** The box must be usable on that phone: inside the safe area and clear of the rounded corners. */
export async function expectInsideSafeArea(locator: Locator, preset: SafeAreaPreset, what = ''): Promise<void> {
    const box = await locator.boundingBox();
    expect(box, `${what || 'element'} has no bounding box`).not.toBeNull();
    const problems = SafeAreas.getProblems(box!, preset);
    expect(problems, `${what || 'element'} at ${JSON.stringify(box)}`).toEqual([]);
}

/** Every visible interactive element of the page that the phone would hide or clip - the whole-page
 *  sweep debugUI.checkSafeAreas runs, so it needs the app's JS to be up. */
export async function findSafeAreaViolations(page: Page, preset: SafeAreaPreset): Promise<SafeAreaViolationInfo[]> {
    return page.evaluate(impl, preset);

    // Runs inside the page: elements can't cross the bridge, so each becomes a short descriptor.
    function impl(preset: SafeAreaPreset): SafeAreaViolationInfo[] {
        interface DebugUIGlobal {
            debugUI?: {
                checkSafeAreas: (preset: SafeAreaPreset) => { element: Element; problems: string[] }[];
            };
        }
        const debugUI = (globalThis as DebugUIGlobal).debugUI;
        if (!debugUI)
            throw new Error('debugUI is not available: the app has not initialized yet');

        return debugUI.checkSafeAreas(preset).map(({ element, problems }) => {
            const id = element.id ? `#${element.id}` : '';
            const classes = element.className && typeof element.className === 'string'
                ? `.${element.className.trim().split(/\s+/).slice(0, 3).join('.')}`
                : '';
            const text = element.textContent.replace(/\s+/g, ' ').trim().slice(0, 40);
            return { target: `${element.tagName.toLowerCase()}${id}${classes}${text ? ` "${text}"` : ''}`, problems };
        });
    }
}
