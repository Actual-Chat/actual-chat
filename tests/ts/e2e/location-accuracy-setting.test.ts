/**
 * E2E test: the "Location accuracy" setting in Settings > Developer tools.
 *
 * Clicking the "Location accuracy" tile cycles the stored setting
 * High -> Balanced -> Low -> High. The displayed value isn't reactive
 * (ComputeState reads a non-Fusion Kvas key), so we re-open the tab after
 * each click to observe the persisted value. Asserts each click advances one
 * step in the ring and that three clicks wrap back to the starting value.
 *
 * Run:
 *   npx vitest run tests/ts/e2e/location-accuracy-setting.test.ts --config vitest.config.e2e.ts
 */

import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import type { Locator, Page } from 'playwright';
import {
    BASE_URL, connectBrowser, dismissCookieConsent, ensureSignedIn, skipOnboarding,
    screenshot, waitForAppReady, type BrowserConnection,
} from './helpers';

const shot = (name: string) => screenshot('e2e', `location-accuracy-${name}`);

const RING = ['High', 'Balanced', 'Low'];
const next = (value: string) => RING[(RING.indexOf(value) + 1) % RING.length];

describe('Location accuracy setting', () => {
    let conn: BrowserConnection;
    let page: Page;

    beforeAll(async () => {
        conn = await connectBrowser();
        page = await conn.context.newPage();
        await ensureSignedIn(page);
    }, 120_000);

    afterAll(async () => {
        await page.close();
        if (conn.ownsBrowser) {
            await conn.context.close();
            await conn.browser.close();
        }
    });

    it('cycles High -> Balanced -> Low -> High on each click', async () => {
        // arrange — read the settled starting value
        let tile = await openLocationAccuracyTile(page);
        const start = await readStable(tile.locator('.tile-item-right'));
        expect(RING).toContain(start);
        await page.screenshot({ path: shot('start') });

        // act/assert — three steps through a 3-element ring must return to the start
        const after1 = next(start);
        const after2 = next(after1);
        const after3 = next(after2);
        for (const expected of [after1, after2, after3]) {
            await tile.click({ force: true });
            await page.waitForTimeout(1_500); // let the batched Kvas write flush
            tile = await openLocationAccuracyTile(page); // re-open: the value isn't reactive
            await expectValue(tile.locator('.tile-item-right'), expected);
        }

        // assert — wrapped back to where we started
        expect(after3).toBe(start);
        await page.screenshot({ path: shot('wrapped') });
    }, 120_000);
});

async function openLocationAccuracyTile(page: Page): Promise<Locator> {
    await page.goto(`${BASE_URL}/settings`, { waitUntil: 'domcontentloaded' });
    await waitForAppReady(page);
    await dismissCookieConsent(page);
    await skipOnboarding(page);

    const devToolsTab = page.locator('.settings-tab-item:has-text("Developer tools")').first();
    await devToolsTab.waitFor({ state: 'visible', timeout: 15_000 });
    await devToolsTab.click({ force: true });

    const tile = page.locator(
        '.settings-tab-content .tile-item:has-text("Location accuracy")',
    ).first();
    await tile.waitFor({ state: 'visible', timeout: 10_000 });
    return tile;
}

// Polls until two consecutive reads agree, so we read the settled (post-ComputeState) value.
async function readStable(value: Locator, timeout = 10_000): Promise<string> {
    const start = Date.now();
    let last = (await value.innerText()).trim();
    while (Date.now() - start < timeout) {
        await value.page().waitForTimeout(300);
        const current = (await value.innerText()).trim();
        if (current === last && RING.includes(current))
            return current;

        last = current;
    }

    return last;
}

async function expectValue(value: Locator, expected: string, timeout = 10_000): Promise<void> {
    const start = Date.now();
    let last = '';
    while (Date.now() - start < timeout) {
        last = (await value.innerText()).trim();
        if (last === expected)
            return;

        await value.page().waitForTimeout(200);
    }

    expect(last).toBe(expected);
}
